export function rebuildCursorConditionsToPredicateTree(flattened) {
    const orGroups = [];

    for (const andSet of flattened) {
        if (!Array.isArray(andSet)) continue;

        const filteredConditions = andSet.filter(cond => !isDummyCondition(cond));
        if (filteredConditions.length === 0) continue;

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

    // Preserve each input AND set as one indivisible DNF branch. Grouping columns by
    // property/operator destroys correlation: (A=1 && B=2) || (A=3 && B=4) must not
    // admit the cross-pairs (1,4) or (3,2).
    if (orGroups.length === 0) {
        return fixpointSimplify({
            nodeType: "logical",
            operator: "Or",
            children: []
        });
    }

    return fixpointSimplify({
        nodeType: "logical",
        operator: "Or",
        children: orGroups
    });
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

    node.children = (node.children ?? []).map(simplify);

    let flattened = [];
    for (const child of node.children) {
        if (child.nodeType === "logical" && child.operator === node.operator) {
            flattened.push(...(child.children ?? []));
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
