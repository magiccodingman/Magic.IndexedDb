"use strict";
import { debugLog } from "./utilityHelpers.js";
import { isValidFilterObject, isValidQueryAdditions } from "./linqValidation.js";
import { QUERY_OPERATIONS } from "./queryConstants.js";

export function initiateNestedOrFilter(nestedOrFilter, queryAdditions, primaryKeys, isUniversalTrue) {
    if (!isValidFilterObject(nestedOrFilter)) {
        throw new Error("Invalid filter object provided to where function.");
    }
    if (!isValidQueryAdditions(queryAdditions)) {
        throw new Error("Invalid addition query provided to where function.");
    }

    nestedOrFilter = cleanNestedOrFilter(nestedOrFilter);
    debugLog("Cleaned Filter Object", { nestedOrFilter });

    let isFilterEmpty = !nestedOrFilter || !nestedOrFilter.orGroups || isUniversalTrue || nestedOrFilter.orGroups.length === 0;

    debugLog("Filter Check After Cleaning", { isFilterEmpty });

    if (isFilterEmpty) {
        if (!queryAdditions || queryAdditions.length === 0) {
            return { isFilterEmpty, nestedOrFilter };
        }
        else {
            debugLog("Empty filter but query additions exist. Applying primary key GREATER_THAN_OR_EQUAL trick.");
            nestedOrFilter = {
                orGroups: [{
                    andGroups: [{
                        conditions: primaryKeys.map(pk => ({
                            property: pk,
                            operation: QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL,
                            value: -Infinity
                        }))
                    }]
                }]
            };
            isFilterEmpty = false;
        }
    }

    return { isFilterEmpty, nestedOrFilter };
}




function cleanNestedOrFilter(filter) {
    if (!filter || !Array.isArray(filter.orGroups)) return null;

    let cleanedOrGroups = filter.orGroups.map(orGroup => {
        if (!Array.isArray(orGroup.andGroups)) return null;

        // Remove empty AND groups
        let cleanedAndGroups = orGroup.andGroups.filter(andGroup =>
            Array.isArray(andGroup.conditions) && andGroup.conditions.length > 0
        );

        return cleanedAndGroups.length > 0 ? { andGroups: cleanedAndGroups } : null;
    }).filter(orGroup => orGroup !== null); // Remove any fully empty OR groups

    return cleanedOrGroups.length > 0 ? { orGroups: cleanedOrGroups } : null;
}