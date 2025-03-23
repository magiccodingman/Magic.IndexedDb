export function rebuildCursorConditionsToPredicateTree(flattened) {
    // Step 1: Normalize to OR of ANDs
    const orRoot = {
        nodeType: "logical",
        operator: "Or",
        children: flattened.map(andArray => ({
            nodeType: "logical",
            operator: "And",
            children: andArray.map(cond => ({
                nodeType: "condition",
                condition: {
                    property: cond.property,
                    operation: cond.operation,
                    value: cond.value,
                    isString: cond.isString ?? false,
                    caseSensitive: cond.caseSensitive ?? false
                }
            }))
        }))
    };

    // Step 2: Simplify redundant ANDs if possible
    const simplified = fixpointSimplify(orRoot);

    return simplified;
}


function extractDynamicIntentGroups(branches) {
    const conditionMap = new Map();

    for (const group of branches) {
        for (const conditionNode of group) {
            const { property, operation, value } = conditionNode.condition;
            const key = `${property}||${operation}`;
            if (!conditionMap.has(key)) conditionMap.set(key, new Set());
            conditionMap.get(key).add(JSON.stringify(conditionNode.condition));
        }
    }

    // Build OR groups per property+operation
    const groupedByProperty = new Map();

    for (const [key, valueSet] of conditionMap.entries()) {
        const [property, op] = key.split("||");
        if (!groupedByProperty.has(property)) groupedByProperty.set(property, []);
        groupedByProperty.get(property).push({
            operation: op,
            values: [...valueSet].map(json => ({
                nodeType: "condition",
                condition: JSON.parse(json)
            }))
        });
    }

    const resultGroups = [];
    for (const [property, ops] of groupedByProperty.entries()) {
        const propertyGroup = {
            nodeType: "logical",
            operator: "Or",
            children: ops.flatMap(o => o.values)
        };
        resultGroups.push(propertyGroup);
    }

    return resultGroups;
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