"use strict";

//  Query Operations
export const QUERY_OPERATIONS = {
    EQUAL: "Equal",
    NOT_EQUAL: "NotEqual",
    GREATER_THAN: "GreaterThan",
    GREATER_THAN_OR_EQUAL: "GreaterThanOrEqual",
    LESS_THAN: "LessThan",
    LESS_THAN_OR_EQUAL: "LessThanOrEqual",
    STARTS_WITH: "StartsWith",
    CONTAINS: "Contains",
    NOT_CONTAINS: "NotContains",
    IN: "In",
    

    NOT_MONTH_EQUAL: "NotMonthEqual",
    MONTH_EQUAL: "MonthEqual",
    MONTH_GREATER_THAN: "MonthGreaterThan",
    MONTH_GREATER_THAN_OR_EQUAL: "MonthGreaterThanOrEqual",
    MONTH_LESS_THAN: "MonthLessThan",
    MONTH_LESS_THAN_OR_EQUAL: "MonthLessThanOrEqual",

    NOT_DAY_EQUAL: "NotDayEqual",
    DAY_EQUAL: "DayEqual",
    DAY_GREATER_THAN: "DayGreaterThan",
    DAY_GREATER_THAN_OR_EQUAL: "DayGreaterThanOrEqual",
    DAY_LESS_THAN: "DayLessThan",
    DAY_LESS_THAN_OR_EQUAL: "DayLessThanOrEqual",
    DAY_OF_WEEK_EQUAL: "DayOfWeekEqual",
    NOT_DAY_OF_WEEK_EQUAL: "NotDayOfWeekEqual",
    DAY_OF_WEEK_GREATER_THAN: "DayOfWeekGreaterThan",
    DAY_OF_WEEK_GREATER_THAN_OR_EQUAL: "DayOfWeekGreaterThanOrEqual",
    DAY_OF_WEEK_LESS_THAN: "DayOfWeekLessThan",
    DAY_OF_WEEK_LESS_THAN_OR_EQUAL: "DayOfWeekLessThanOrEqual",

    DAY_OF_YEAR_EQUAL: "DayOfYearEqual",
    NOT_DAY_OF_YEAR_EQUAL: "NotDayOfYearEqual",
    DAY_OF_YEAR_GREATER_THAN: "DayOfYearGreaterThan",
    DAY_OF_YEAR_GREATER_THAN_OR_EQUAL: "DayOfYearGreaterThanOrEqual",
    DAY_OF_YEAR_LESS_THAN: "DayOfYearLessThan",
    DAY_OF_YEAR_LESS_THAN_OR_EQUAL: "DayOfYearLessThanOrEqual",

    YEAR_EQUAL: "YearEqual",
    NOT_YEAR_EQUAL: "NotYearEqual",
    YEAR_GREATER_THAN: "YearGreaterThan",
    YEAR_GREATER_THAN_OR_EQUAL: "YearGreaterThanOrEqual",
    YEAR_LESS_THAN: "YearLessThan",
    YEAR_LESS_THAN_OR_EQUAL: "YearLessThanOrEqual",



    ENDS_WITH: "EndsWith",
    NOT_ENDS_WITH: "NotEndsWith",
    NOT_STARTS_WITH: "NotStartsWith",

    NOT_LENGTH_EQUAL: "NotLengthEqual",
    LENGTH_EQUAL: "LengthEqual",
    LENGTH_GREATER_THAN: "LengthGreaterThan",
    LENGTH_GREATER_THAN_OR_EQUAL: "LengthGreaterThanOrEqual",
    LENGTH_LESS_THAN: "LengthLessThan",
    LENGTH_LESS_THAN_OR_EQUAL: "LengthLessThanOrEqual",

    TYPEOF_NUMBER: "TypeOfNumber",
    TYPEOF_STRING: "TypeOfString",
    TYPEOF_DATE: "TypeOfDate",
    TYPEOF_ARRAY: "TypeOfArray",
    TYPEOF_OBJECT: "TypeOfObject",
    TYPEOF_BLOB: "TypeOfBlob",
    TYPEOF_ARRAYBUFFER: "TypeOfArrayBuffer",
    TYPEOF_FILE: "TypeOfFile",


    NOT_TYPEOF_NUMBER: "NotTypeOfNumber",
    NOT_TYPEOF_STRING: "NotTypeOfString",
    NOT_TYPEOF_DATE: "NotTypeOfDate",
    NOT_TYPEOF_ARRAY: "NotTypeOfArray",
    NOT_TYPEOF_OBJECT: "NotTypeOfObject",
    NOT_TYPEOF_BLOB: "NotTypeOfBlob",
    NOT_TYPEOF_ARRAYBUFFER: "NotTypeOfArrayBuffer",
    NOT_TYPEOF_FILE: "NotTypeOfFile",

    IS_NULL: "IsNull",
    IS_NOT_NULL: "IsNotNull",

};

export const OPERATION_CLASSES = {
    EQUALITY: new Set(["Equal", "NotEqual", "In"]),
    NULL_CHECK: new Set(["IsNull", "IsNotNull"]),
    RANGE: new Set(["GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual"]),
    STRING_CONTAINS: new Set(["Contains", "NotContains"]),
    STRING_EDGE: new Set(["StartsWith", "NotStartsWith", "EndsWith", "NotEndsWith"]),
    LENGTH: new Set([
        "LengthEqual", "NotLengthEqual",
        "LengthGreaterThan", "LengthGreaterThanOrEqual",
        "LengthLessThan", "LengthLessThanOrEqual"
    ]),
    TYPE: new Set([
        "TypeOfNumber", "NotTypeOfNumber",
        "TypeOfString", "NotTypeOfString",
        "TypeOfDate", "NotTypeOfDate",
        "TypeOfArray", "NotTypeOfArray",
        "TypeOfObject", "NotTypeOfObject",
        "TypeOfBlob", "NotTypeOfBlob",
        "TypeOfArrayBuffer", "NotTypeOfArrayBuffer",
        "TypeOfFile", "NotTypeOfFile"
    ])
};



//  Query Additions (Sorting & Pagination)
export const QUERY_ADDITIONS = {
    ORDER_BY: "orderBy",
    ORDER_BY_DESCENDING: "orderByDescending",
    FIRST: "first",
    LAST: "last",
    SKIP: "skip",
    TAKE: "take",
    TAKE_LAST: "takeLast",
    STABLE_ORDERING: "stableOrdering",
};

//  Query Combination Ruleset (What Can Be Combined in AND `&&`)
export const QUERY_COMBINATION_RULES = {
    [QUERY_OPERATIONS.EQUAL]: [QUERY_OPERATIONS.EQUAL, QUERY_OPERATIONS.IN],
    [QUERY_OPERATIONS.GREATER_THAN]: [QUERY_OPERATIONS.LESS_THAN, QUERY_OPERATIONS.LESS_THAN_OR_EQUAL],
    [QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL]: [QUERY_OPERATIONS.LESS_THAN, QUERY_OPERATIONS.LESS_THAN_OR_EQUAL],
    [QUERY_OPERATIONS.LESS_THAN]: [QUERY_OPERATIONS.GREATER_THAN, QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL],
    [QUERY_OPERATIONS.LESS_THAN_OR_EQUAL]: [QUERY_OPERATIONS.GREATER_THAN, QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL],
    [QUERY_OPERATIONS.STARTS_WITH]: [], //  Cannot combine StartsWith with anything
    [QUERY_OPERATIONS.IN]: [QUERY_OPERATIONS.EQUAL],
    [QUERY_OPERATIONS.CONTAINS]: [], //  Must always go to a cursor
};

//  Query Addition Ruleset (Which Operations Can Be Stacked)
export const QUERY_ADDITION_RULES = {
    [QUERY_ADDITIONS.ORDER_BY]: [QUERY_ADDITIONS.SKIP, QUERY_ADDITIONS.TAKE, QUERY_ADDITIONS.TAKE_LAST],
    [QUERY_ADDITIONS.ORDER_BY_DESCENDING]: [QUERY_ADDITIONS.SKIP, QUERY_ADDITIONS.TAKE, QUERY_ADDITIONS.TAKE_LAST],
    [QUERY_ADDITIONS.FIRST]: [QUERY_ADDITIONS.ORDER_BY, QUERY_ADDITIONS.ORDER_BY_DESCENDING],
    [QUERY_ADDITIONS.LAST]: [QUERY_ADDITIONS.ORDER_BY, QUERY_ADDITIONS.ORDER_BY_DESCENDING],
    [QUERY_ADDITIONS.SKIP]: [QUERY_ADDITIONS.ORDER_BY, QUERY_ADDITIONS.ORDER_BY_DESCENDING],
    [QUERY_ADDITIONS.TAKE]: [QUERY_ADDITIONS.ORDER_BY, QUERY_ADDITIONS.ORDER_BY_DESCENDING],
    [QUERY_ADDITIONS.TAKE_LAST]: [QUERY_ADDITIONS.ORDER_BY, QUERY_ADDITIONS.ORDER_BY_DESCENDING], // **Allow TAKE_LAST after ORDER_BY**
};