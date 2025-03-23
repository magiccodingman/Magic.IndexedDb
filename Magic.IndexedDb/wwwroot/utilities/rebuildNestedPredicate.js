export function rebuildCursorConditionsToPredicateTree(flattened) {
    const orGroups = [];

    for (const andSet of flattened) {
        const filteredConditions = andSet.filter(cond => !isDummyCondition(cond));
        if (filteredConditions.length === 0) continue; // Skip this group if all conditions were dummy

        const andGroup = {
            nodeType: "logical",
            operator: "And",
            children: filteredConditions.map(condition => ({
                nodeType: "condition",
                condition: {
                    property: condition.property,
                    operation: condition.operation,
                    value: condition.value,
                    isString: condition.isString ?? false,
                    caseSensitive: condition.caseSensitive ?? false
                }
            }))
        };

        orGroups.push(andGroup);
    }

    // Handle the case where ALL conditions were dummy: this means "match everything"
    if (orGroups.length === 0) {
        return fixpointSimplify({
            nodeType: "logical",
            operator: "Or",
            children: []
        });
    }

    const collapsed = collapseOrGroups(orGroups);

    return fixpointSimplify({
        nodeType: "logical",
        operator: "Or",
        children: collapsed
    });
}

function collapseOrGroups(orChildren) {
    const buckets = new Map();
    const results = [];

    for (const node of orChildren) {
        if (node.operator !== "And") {
            results.push(node);
            continue;
        }

        const simpleKeys = [];
        const complexNodes = [];

        for (const child of node.children) {
            if (child.nodeType === "condition") {
                const key = `${child.condition.property}||${child.condition.operation}`;
                simpleKeys.push({ key, node: child });
            } else {
                complexNodes.push(child);
            }
        }

        const groupKey = simpleKeys.map(s => s.key).sort().join("&&");

        if (!buckets.has(groupKey)) {
            buckets.set(groupKey, []);
        }

        buckets.get(groupKey).push(node);
    }

    // Build optimized OR sets per bucket
    for (const group of buckets.values()) {
        if (group.length === 1) {
            results.push(group[0]);
            continue;
        }

        // Find which conditions are shared across all ANDs
        const conditionMatrix = group.map(g => g.children);
        const transposed = transpose(conditionMatrix);

        const collapsedAnd = {
            nodeType: "logical",
            operator: "And",
            children: []
        };

        for (const col of transposed) {
            // If all columns refer to the same property/operator but different values
            const base = col[0];
            const allSamePO = col.every(c =>
                c.nodeType === "condition" &&
                c.condition.property === base.condition.property &&
                c.condition.operation === base.condition.operation
            );

            if (allSamePO) {
                // Build OR group of all values
                const orGroup = {
                    nodeType: "logical",
                    operator: "Or",
                    children: col
                };
                collapsedAnd.children.push(orGroup);
            } else {
                // They differ structurally, preserve individually
                collapsedAnd.children.push(...col);
            }
        }

        results.push(collapsedAnd);
    }

    return results;
}


function transpose(matrix) {
    if (!matrix.length) return [];
    const len = Math.max(...matrix.map(row => row.length));
    const transposed = Array.from({ length: len }, (_, i) =>
        matrix.map(row => row[i]).filter(Boolean)
    );
    return transposed;
}

function fixpointSimplify(node) {
    while (true) {
        const before = JSON.stringify(node);
        node = simplify(node);
        const after = JSON.stringify(node);
        if (before === after) break;
    }
    return node;
}

function simplify(node) {
    if (node.nodeType === "condition") return node;

    node.children = node.children.map(simplify);

    let flattened = [];
    for (const child of node.children) {
        if (child.nodeType === "logical" && child.operator === node.operator) {
            flattened.push(...child.children);
        } else {
            flattened.push(child);
        }
    }

    node.children = flattened;

    const seen = new Set();
    node.children = node.children.filter(ch => {
        const key = JSON.stringify(ch);
        if (seen.has(key)) return false;
        seen.add(key);
        return true;
    });

    if (node.children.length === 1) {
        return node.children[0];
    }

    return node;
}


function isDummyCondition(cond) {
    if (typeof cond.value === "number") {
        if ((cond.value === Infinity || cond.value === -Infinity) &&
            ["GreaterThanOrEqual", "LessThanOrEqual", "GreaterThan", "LessThan"].includes(cond.operation)) {
            return true;
        }
    }
    return false;
}
