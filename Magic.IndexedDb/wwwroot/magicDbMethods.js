"use strict";

import {
    setDebugMode,
    getLastQueryPlannerTrace as getPlannerTrace,
    clearQueryPlannerTrace as clearPlannerTrace
} from "./utilities/utilityHelpers.js";

const moduleCache = new Map(); // Cache for dynamically imported modules

export function configureDebug(enabled) {
    setDebugMode(enabled);
}

export function getLastQueryPlannerTrace() {
    return getPlannerTrace();
}

export function clearQueryPlannerTrace() {
    clearPlannerTrace();
}

async function getModule(modulePath) {
    if (moduleCache.has(modulePath)) {
        return moduleCache.get(modulePath);
    }

    try {
        const importedModule = await import(modulePath);
        moduleCache.set(modulePath, importedModule);
        return importedModule;
    } catch (error) {
        console.error(
            `Failed to import module: ${modulePath}\nError Message: ${error.message}\nStack Trace: ${error.stack}`
        );

        if (error instanceof SyntaxError) {
            console.error("Possible Syntax Error in the module.");
        } else if (error instanceof TypeError) {
            console.error("Possible TypeError in the module (maybe missing export?).");
        }

        throw new Error(`Module import error: ${modulePath}`);
    }
}


export async function JsHandler(isVoid, modulePath, methodName, parameters) {
    try {
        const module = await getModule(modulePath);

        if (typeof module[methodName] !== "function") {
            throw new Error(`Method '${methodName}' not found in module '${modulePath}'`);
        }

        const result = await module[methodName](...parameters);
        return isVoid ? true : result;
    } catch (error) {
        console.error(`JsHandler error calling ${methodName} from ${modulePath}:`, error);
        throw error;
    }
}

export async function streamedJsHandler(streamRef, instanceId, dotNetHelper, maxChunkBytes) {
    if (!streamRef || typeof streamRef.arrayBuffer !== "function") {
        throw new TypeError("Invalid stream reference received.");
    }

    try {
        // Decode incoming data
        let arrayBuffer = await streamRef.arrayBuffer();
        let jsonText = new TextDecoder().decode(arrayBuffer);
        arrayBuffer = null; // Free memory

        // Parse the JSON payload
        let parsedData = JSON.parse(jsonText);
        jsonText = null; // Free memory

        let { protocolVersion = 1, modulePath, methodName, isVoid, yieldResults, parameters = [] } = parsedData;

        // Validate modulePath
        if (!modulePath || typeof modulePath !== "string") {
            throw new TypeError("Invalid module path received.");
        }

        // Dynamically import the module
        const targetModule = await getModule(modulePath);
        if (typeof targetModule[methodName] !== "function") {
            throw new Error(`Method '${methodName}' not found in ${modulePath}.`);
        }

        // Protocol v1 encoded every argument as a JSON string. Version 2 carries
        // actual JSON values and avoids the extra parse/copy for every invocation.
        let safeParameters = protocolVersion >= 2
            ? parameters
            : parameters.map(param => JSON.parse(param));

        // If yielding results, stream asynchronously
        if (yieldResults) {
            const resultIterator = targetModule[methodName](...safeParameters);

            if (resultIterator && typeof resultIterator[Symbol.asyncIterator] === "function") {
                let yieldOrderIndex = 0;

                try {
                    for await (const item of resultIterator) {
                        let jsonChunk = JSON.stringify(item);
                        let chunkInstanceId = crypto.randomUUID(); // Unique ID for this yielded item
                        let chunks = chunkString(jsonChunk, maxChunkBytes);

                        for (let i = 0; i < chunks.length; i++) {
                            await dotNetHelper.invokeMethodAsync(
                                "ProcessJsChunk",
                                instanceId,
                                chunkInstanceId,
                                yieldOrderIndex,
                                chunks[i],
                                i,
                                chunks.length
                            );
                        }

                        yieldOrderIndex++; // Ensure the next item keeps order
                    }

                    // Notify Blazor that streaming is done
                    await dotNetHelper.invokeMethodAsync("ProcessJsChunk", instanceId, "STREAM_COMPLETE", -1, "", 0, 1);
                } catch (error) {
                    console.error("Streaming error:", error);
                    throw error;
                }
            }
            return;
        }

        // Normal execution (only await for non-yielding functions)
        let result = await targetModule[methodName](...safeParameters);
        safeParameters = null; // Free memory after function call

        // If `isVoid`, return an empty confirmation response
        if (isVoid) {
            return new Uint8Array(new TextEncoder().encode("true"));
        }

        // Ensure result is a valid JSON response
        let responseJson = JSON.stringify(result === undefined ? null : result);
        let encodedResponse = new TextEncoder().encode(responseJson);
        return new Uint8Array(encodedResponse);
    } catch (error) {
        console.error("Error handling streamed JS:", error);
        throw error;
    }
}


// Split on Unicode code-point boundaries while measuring the actual UTF-8 bytes
// sent over interop. JavaScript string length counts UTF-16 code units instead.
function chunkString(str, size) {
    const maxBytes = Number.isFinite(size) && size > 0 ? size : 31 * 1024;
    const encoder = new TextEncoder();
    const chunks = [];
    let currentChunk = "";
    let currentBytes = 0;

    for (const codePoint of str) {
        const codePointBytes = encoder.encode(codePoint).byteLength;
        if (currentChunk && currentBytes + codePointBytes > maxBytes) {
            chunks.push(currentChunk);
            currentChunk = "";
            currentBytes = 0;
        }

        currentChunk += codePoint;
        currentBytes += codePointBytes;
    }

    if (currentChunk || str.length === 0) chunks.push(currentChunk);
    return chunks;
}
