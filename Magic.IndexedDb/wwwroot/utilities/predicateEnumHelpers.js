"use strict";

export const NodeType = {
    Logical: 0,
    Condition: 1
};

export const LogicalOperator = {
    And: 0,
    Or: 1
};

/**
 * Resolves node type (enum or string) to a normalized string.
 * Returns null if the input is null/undefined.
 */
export function resolveNodeType(value) {
    if (value === null || value === undefined) return null;
    if (value === NodeType.Logical || value === "Logical") return "Logical";
    if (value === NodeType.Condition || value === "Condition") return "Condition";
    throw new Error(`Unknown node type: ${value}`);
}

/**
 * Resolves logical operator (enum or string) to a normalized string.
 * Returns null if the input is null/undefined.
 */
export function resolveLogicalOperator(value) {
    if (value === null || value === undefined) return null;
    if (value === LogicalOperator.And || value === "And") return "And";
    if (value === LogicalOperator.Or || value === "Or") return "Or";
    throw new Error(`Unknown logical operator: ${value}`);
}
