import { QUERY_OPERATIONS } from "./queryConstants.js";

/**
 * Returns false only when two AND fragments are provably contradictory.
 *
 * This function is an optimization gate, not the predicate evaluator. A false
 * "compatible" answer costs some work; a false "incompatible" answer deletes
 * valid results. Keep the proof rules deliberately conservative.
 */
export function areConditionsCompatible(groupA, groupB) {
    const merged = normalizeConditions([...groupA, ...groupB]);
    const byProperty = new Map();

    for (const condition of merged) {
        if (!condition || !condition.property) continue;
        if (!byProperty.has(condition.property)) {
            byProperty.set(condition.property, []);
        }
        byProperty.get(condition.property).push(condition);
    }

    for (const conditions of byProperty.values()) {
        if (hasProvableNullConflict(conditions)) return false;

        // Ignore-case string operations broaden the accepted value set. Unless we can
        // prove a contradiction under those exact semantics, do not prune the branch.
        const hasCaseInsensitiveString = conditions.some(condition =>
            condition.isString === true && condition.caseSensitive === false);

        if (!hasCaseInsensitiveString) {
            if (hasProvableEqualityConflict(conditions)) return false;
            if (hasProvableStringConflict(conditions)) return false;
        }

        if (hasProvableNumericRangeConflict(conditions)) return false;
    }

    return true;
}

function normalizeConditions(conditions) {
    return conditions.map(condition => {
        const normalized = { ...condition };

        if (normalized.operation === QUERY_OPERATIONS.NOT_EQUAL &&
            (normalized.value === null || normalized.value === undefined)) {
            normalized.operation = QUERY_OPERATIONS.IS_NOT_NULL;
        } else if (normalized.operation === QUERY_OPERATIONS.EQUAL &&
            (normalized.value === null || normalized.value === undefined)) {
            normalized.operation = QUERY_OPERATIONS.IS_NULL;
        }

        return normalized;
    });
}

function hasProvableNullConflict(conditions) {
    const hasNull = conditions.some(c => c.operation === QUERY_OPERATIONS.IS_NULL);
    const hasNotNull = conditions.some(c => c.operation === QUERY_OPERATIONS.IS_NOT_NULL);
    return hasNull && hasNotNull;
}

function hasProvableEqualityConflict(conditions) {
    const equals = conditions.filter(c => c.operation === QUERY_OPERATIONS.EQUAL);
    const notEquals = conditions.filter(c => c.operation === QUERY_OPERATIONS.NOT_EQUAL);
    const inConditions = conditions.filter(c => c.operation === QUERY_OPERATIONS.IN && Array.isArray(c.value));

    if (equals.length > 1) {
        const first = valueKey(equals[0].value);
        if (equals.some(condition => valueKey(condition.value) !== first)) {
            return true;
        }
    }

    for (const equal of equals) {
        if (notEquals.some(notEqual => valuesEqual(equal.value, notEqual.value))) {
            return true;
        }

        for (const inCondition of inConditions) {
            if (!inCondition.value.some(value => valuesEqual(value, equal.value))) {
                return true;
            }
        }
    }

    return false;
}

function hasProvableNumericRangeConflict(conditions) {
    const rangeConditions = conditions.filter(condition =>
        typeof condition.value === "number" &&
        [
            QUERY_OPERATIONS.GREATER_THAN,
            QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL,
            QUERY_OPERATIONS.LESS_THAN,
            QUERY_OPERATIONS.LESS_THAN_OR_EQUAL
        ].includes(condition.operation));

    if (rangeConditions.length === 0) return false;

    let min = -Infinity;
    let max = Infinity;
    let minInclusive = true;
    let maxInclusive = true;

    for (const condition of rangeConditions) {
        const value = condition.value;
        switch (condition.operation) {
            case QUERY_OPERATIONS.GREATER_THAN:
                if (value > min || (value === min && minInclusive)) {
                    min = value;
                    minInclusive = false;
                }
                break;
            case QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL:
                if (value > min) {
                    min = value;
                    minInclusive = true;
                }
                break;
            case QUERY_OPERATIONS.LESS_THAN:
                if (value < max || (value === max && maxInclusive)) {
                    max = value;
                    maxInclusive = false;
                }
                break;
            case QUERY_OPERATIONS.LESS_THAN_OR_EQUAL:
                if (value < max) {
                    max = value;
                    maxInclusive = true;
                }
                break;
        }
    }

    if (min > max) return true;
    if (min === max && (!minInclusive || !maxInclusive)) return true;

    const numericEquals = conditions.filter(condition =>
        condition.operation === QUERY_OPERATIONS.EQUAL && typeof condition.value === "number");

    return numericEquals.some(condition => {
        const value = condition.value;
        return value < min || value > max ||
            (value === min && !minInclusive) ||
            (value === max && !maxInclusive);
    });
}

function hasProvableStringConflict(conditions) {
    const equals = conditions.filter(condition =>
        condition.operation === QUERY_OPERATIONS.EQUAL && typeof condition.value === "string");

    for (const equal of equals) {
        const value = equal.value;

        for (const condition of conditions) {
            if (typeof condition.value !== "string") continue;

            switch (condition.operation) {
                case QUERY_OPERATIONS.STARTS_WITH:
                    if (!value.startsWith(condition.value)) return true;
                    break;
                case QUERY_OPERATIONS.NOT_STARTS_WITH:
                    if (value.startsWith(condition.value)) return true;
                    break;
                case QUERY_OPERATIONS.ENDS_WITH:
                    if (!value.endsWith(condition.value)) return true;
                    break;
                case QUERY_OPERATIONS.NOT_ENDS_WITH:
                    if (value.endsWith(condition.value)) return true;
                    break;
                case QUERY_OPERATIONS.CONTAINS:
                    if (!value.includes(condition.value)) return true;
                    break;
                case QUERY_OPERATIONS.NOT_CONTAINS:
                    if (value.includes(condition.value)) return true;
                    break;
            }
        }
    }

    return false;
}

function valuesEqual(left, right) {
    return valueKey(left) === valueKey(right);
}

function valueKey(value) {
    return JSON.stringify(value);
}
