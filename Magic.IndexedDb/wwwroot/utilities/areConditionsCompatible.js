import { OPERATION_CLASSES, QUERY_OPERATIONS } from "./queryConstants.js";

/**
 * Merges two AND groups (groupA, groupB) and checks if they produce
 * a logically impossible combination. If there's a guaranteed conflict,
 * returns false. Otherwise, returns true.
 */
export function areConditionsCompatible(groupA, groupB) {
    // 1) Combine and normalize
    const merged = normalizeConditions([...groupA, ...groupB]);

    // 2) Group conditions by property
    const byProperty = new Map();
    for (const cond of merged) {
        if (!byProperty.has(cond.property)) {
            byProperty.set(cond.property, []);
        }
        byProperty.get(cond.property).push(cond);
    }

    // 3) Check each property set for guaranteed conflicts
    for (const [prop, conditions] of byProperty.entries()) {
        // Classify them by operation category for easy extension
        const classes = classifyOperations(conditions);

        //=== [A] Check multiple EQUAL conflicts ===
        if (!checkMultipleEqualConflict(classes.EQUALITY)) {
            return false;
        }

        //=== [B] Check EQUAL vs NOT_EQUAL / IN conflicts ===
        if (!checkEqualityVersusNotEquality(classes.EQUALITY)) {
            return false;
        }

        //=== [C] Check NULL conflicts (IsNull + IsNotNull) ===
        if (!checkNullConflicts(classes.NULL_CHECK)) {
            return false;
        }

        //=== [D] Check TypeOf / NotTypeOf conflicts ===
        if (detectConflictingTypeOps(classes.TYPE)) {
            return false;
        }

        //=== [E] Check Range conflicts (Range vs. Range, Range vs. EQUAL, etc.) ===
        if (!checkRangeConflicts(classes.RANGE, classes.EQUALITY)) {
            return false;
        }

        //=== [F] Check string-based conflicts (EQUAL + StartsWith, etc.) ===
        if (!checkStringConflicts(classes, prop)) {
            return false;
        }

        //=== [G] Check length-based logic if desired (LENGTH ops) ===
        if (!checkLengthConflicts(classes.LENGTH, classes.EQUALITY)) {
            return false;
        }
    }

    // If we got here, no guaranteed conflict
    return true;
}

/**
 * 1) Normalize certain conditions:
 *    - NotEqual(null) => IsNotNull
 *    - Equal(null) => IsNull
 *    - Possibly unify EQUAL + "CaseSensitive" or "caseSensitive=false" if you want
 */
function normalizeConditions(conditions) {
    return conditions.map(c => {
        // Make a safe clone
        const cond = { ...c };

        // If (NotEqual, null) -> IsNotNull
        if (
            cond.operation === QUERY_OPERATIONS.NOT_EQUAL &&
            (cond.value === null || cond.value === undefined)
        ) {
            cond.operation = QUERY_OPERATIONS.IS_NOT_NULL;
            // optional: cond.value = null; (value is irrelevant for IS_NOT_NULL)
        }

        // If (Equal, null) -> IsNull
        else if (
            cond.operation === QUERY_OPERATIONS.EQUAL &&
            (cond.value === null || cond.value === undefined)
        ) {
            cond.operation = QUERY_OPERATIONS.IS_NULL;
        }

        return cond;
    });
}

/**
 * Groups conditions by operation class (EQUALITY, RANGE, STRING, etc.)
 * according to OPERATION_CLASSES.
 */
function classifyOperations(conditions) {
    const classes = {
        EQUALITY: [],
        NULL_CHECK: [],
        RANGE: [],
        STRING_CONTAINS: [],
        STRING_EDGE: [],
        LENGTH: [],
        TYPE: [],
        UNKNOWN: []
    };

    for (const cond of conditions) {
        const op = cond.operation;

        if (OPERATION_CLASSES.EQUALITY.has(op)) {
            classes.EQUALITY.push(cond);
        }
        else if (OPERATION_CLASSES.NULL_CHECK.has(op)) {
            classes.NULL_CHECK.push(cond);
        }
        else if (OPERATION_CLASSES.RANGE.has(op)) {
            classes.RANGE.push(cond);
        }
        else if (OPERATION_CLASSES.STRING_CONTAINS.has(op)) {
            classes.STRING_CONTAINS.push(cond);
        }
        else if (OPERATION_CLASSES.STRING_EDGE.has(op)) {
            classes.STRING_EDGE.push(cond);
        }
        else if (OPERATION_CLASSES.LENGTH.has(op)) {
            classes.LENGTH.push(cond);
        }
        else if (OPERATION_CLASSES.TYPE.has(op)) {
            classes.TYPE.push(cond);
        }
        else {
            classes.UNKNOWN.push(cond);
        }
    }

    return classes;
}

/** [A] EQUAL vs multiple EQUAL conflict */
function checkMultipleEqualConflict(equalityOps) {
    // e.g., prop == "Luna" and prop == "Jerry" => conflict
    const equals = equalityOps.filter(o => o.operation === QUERY_OPERATIONS.EQUAL);
    if (equals.length > 1) {
        // if they have different values => conflict
        const distinctVals = new Set(equals.map(e => JSON.stringify(e.value)));
        if (distinctVals.size > 1) {
            return false;
        }
    }
    return true;
}

/** [B] EQUAL vs NOT_EQUAL / IN conflicts */
function checkEqualityVersusNotEquality(equalityOps) {
    const equals = equalityOps.filter(o => o.operation === QUERY_OPERATIONS.EQUAL);
    const notEquals = equalityOps.filter(o => o.operation === QUERY_OPERATIONS.NOT_EQUAL);
    const inOps = equalityOps.filter(o => o.operation === QUERY_OPERATIONS.IN);

    // EQUAL vs NOT_EQUAL (same value => conflict)
    for (const eq of equals) {
        if (notEquals.some(ne => JSON.stringify(ne.value) === JSON.stringify(eq.value))) {
            return false;
        }
    }

    // EQUAL vs IN (eq.value must be in the array) or conflict
    for (const eq of equals) {
        for (const iop of inOps) {
            if (!Array.isArray(iop.value)) {
                // malformed? skip
                continue;
            }
            const eqValString = JSON.stringify(eq.value);
            const inSetString = iop.value.map(v => JSON.stringify(v));
            if (!inSetString.includes(eqValString)) {
                // eq.value not in the array => conflict
                return false;
            }
        }
    }

    // NOT_EQUAL vs IN => if the IN set has only 1 value, which is exactly the notEquals, that might not be a conflict.
    // BUT if we had multiple NotEquals that exclude everything in the In set, that can be a conflict. 
    // This quickly becomes more complex — you can add logic as needed.

    return true;
}

/** [C] Check for IsNull vs IsNotNull conflict */
function checkNullConflicts(nullOps) {
    const hasNull = nullOps.some(c => c.operation === QUERY_OPERATIONS.IS_NULL);
    const hasNotNull = nullOps.some(c => c.operation === QUERY_OPERATIONS.IS_NOT_NULL);
    if (hasNull && hasNotNull) {
        // definitely a conflict
        return false;
    }
    return true;
}

/** [D] Basic conflict check for type operations (TypeOfX + NotTypeOfX on same property => conflict) */
function detectConflictingTypeOps(typeOps) {
    const typeSet = new Set(typeOps.map(c => c.operation));
    for (const op of typeSet) {
        if (op.startsWith("TypeOf")) {
            const negated = op.replace("TypeOf", "NotTypeOf");
            if (typeSet.has(negated)) return true;
        }
        else if (op.startsWith("NotTypeOf")) {
            const positive = op.replace("NotTypeOf", "TypeOf");
            if (typeSet.has(positive)) return true;
        }
    }
    return false;
}

/** [E] Range Conflicts (Range vs Range, Range vs EQUAL, etc.) */
function checkRangeConflicts(rangeOps, equalityOps) {
    // Basic approach:
    // 1) If there's an EQUAL => check if EQUAL value is in the combined range
    // 2) If multiple range ops => see if intersection is empty
    // For full correctness, you'd parse them into a min..max and see if there's overlap.

    // Step 1: Merge all range ops into [min, max] if possible
    const rangeData = {
        min: -Infinity,
        max: Infinity,
        minInclusive: true,
        maxInclusive: true
    };

    for (const r of rangeOps) {
        const val = r.value;
        if (r.operation === QUERY_OPERATIONS.GREATER_THAN) {
            if (val >= rangeData.min && !rangeData.minInclusive) {
                // already strictly greater
                if (val > rangeData.min) {
                    rangeData.min = val;
                    rangeData.minInclusive = false;
                }
            } else if (val > rangeData.min) {
                rangeData.min = val;
                rangeData.minInclusive = false;
            }
        }
        else if (r.operation === QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL) {
            if (val > rangeData.min) {
                rangeData.min = val;
            }
            // If previously was strict, keep it if val is the same
        }
        else if (r.operation === QUERY_OPERATIONS.LESS_THAN) {
            if (val <= rangeData.max && !rangeData.maxInclusive) {
                // already strictly less
                if (val < rangeData.max) {
                    rangeData.max = val;
                    rangeData.maxInclusive = false;
                }
            } else if (val < rangeData.max) {
                rangeData.max = val;
                rangeData.maxInclusive = false;
            }
        }
        else if (r.operation === QUERY_OPERATIONS.LESS_THAN_OR_EQUAL) {
            if (val < rangeData.max) {
                rangeData.max = val;
            }
        }
    }

    // If min > max => conflict
    if (rangeData.min > rangeData.max) {
        return false;
    }
    if (rangeData.min === rangeData.max && !rangeData.minInclusive && !rangeData.maxInclusive) {
        // means > X and < X => conflict
        return false;
    }

    // Step 2: If there's an EQUAL => check if it's within [min..max]
    const eqOps = equalityOps.filter(e => e.operation === QUERY_OPERATIONS.EQUAL);
    for (const eq of eqOps) {
        const val = eq.value;
        if (typeof val !== "number") {
            // If the property is numeric range but eq is string => conflict or not?
            // Up to you to decide. Let's skip for now.
            continue;
        }
        // check lower bound
        if (val < rangeData.min || (val === rangeData.min && !rangeData.minInclusive)) {
            return false;
        }
        // check upper bound
        if (val > rangeData.max || (val === rangeData.max && !rangeData.maxInclusive)) {
            return false;
        }
    }

    return true;
}

/** [F] Basic string conflict checks: EQUAL vs STARTS_WITH/ENDS_WITH/CONTAINS, etc. */
function checkStringConflicts(classes, prop) {
    const { EQUALITY, STRING_CONTAINS, STRING_EDGE } = classes;

    // For example: EQUAL("Luna") + StartsWith("J") => conflict
    // But EQUAL("Luna") + Contains("a") => not a conflict (actually "Luna" does have "a")
    // This can get fancy if you handle caseSensitive logic, but let's keep it short.

    const eqOps = EQUALITY.filter(e => e.operation === QUERY_OPERATIONS.EQUAL && typeof e.value === "string");
    for (const eq of eqOps) {
        const eqVal = eq.value;

        // Check vs STRING_EDGE
        for (const edgeOp of STRING_EDGE) {
            if (typeof edgeOp.value !== "string") continue;

            if (edgeOp.operation === QUERY_OPERATIONS.STARTS_WITH) {
                if (!eqVal.startsWith(edgeOp.value)) {
                    return false;
                }
            }
            if (edgeOp.operation === QUERY_OPERATIONS.NOT_STARTS_WITH) {
                if (eqVal.startsWith(edgeOp.value)) {
                    return false;
                }
            }
            if (edgeOp.operation === QUERY_OPERATIONS.ENDS_WITH) {
                if (!eqVal.endsWith(edgeOp.value)) {
                    return false;
                }
            }
            if (edgeOp.operation === QUERY_OPERATIONS.NOT_ENDS_WITH) {
                if (eqVal.endsWith(edgeOp.value)) {
                    return false;
                }
            }
        }

        // Check vs STRING_CONTAINS
        for (const containsOp of STRING_CONTAINS) {
            if (typeof containsOp.value !== "string") continue;

            if (containsOp.operation === QUERY_OPERATIONS.CONTAINS) {
                if (!eqVal.includes(containsOp.value)) {
                    return false;
                }
            }
            else if (containsOp.operation === QUERY_OPERATIONS.NOT_CONTAINS) {
                if (eqVal.includes(containsOp.value)) {
                    return false;
                }
            }
        }
    }

    return true;
}

/** [G] Basic length-based conflict detection. EQUAL("abc") + LENGTH_LESS_THAN(2) => conflict, etc. */
function checkLengthConflicts(lengthOps, equalityOps) {
    // We only do a trivial check with EQUAL string or array. 
    // If you want array length checks for arrays, you'd add more logic. 
    const eqStrings = equalityOps.filter(e => e.operation === QUERY_OPERATIONS.EQUAL && typeof e.value === "string");

    for (const eq of eqStrings) {
        const strLen = eq.value.length;
        for (const lOp of lengthOps) {
            const val = +lOp.value; // numeric length
            if (isNaN(val)) continue;

            switch (lOp.operation) {
                case "LengthEqual":
                    if (strLen !== val) return false;
                    break;
                case "NotLengthEqual":
                    if (strLen === val) return false;
                    break;
                case "LengthGreaterThan":
                    if (!(strLen > val)) return false;
                    break;
                case "LengthGreaterThanOrEqual":
                    if (!(strLen >= val)) return false;
                    break;
                case "LengthLessThan":
                    if (!(strLen < val)) return false;
                    break;
                case "LengthLessThanOrEqual":
                    if (!(strLen <= val)) return false;
                    break;
            }
        }
    }

    return true;
}
