"use strict";

const moduleCache = new Map(); // Cache for dynamically imported modules

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
        console.error("Invalid stream reference received.");
        return new Uint8Array();
    }

    try {
        // Decode incoming data
        let arrayBuffer = await streamRef.arrayBuffer();
        let jsonText = new TextDecoder().decode(arrayBuffer);
        arrayBuffer = null; // Free memory

        // Parse the JSON payload
        let parsedData = JSON.parse(jsonText);
        jsonText = null; // Free memory

        let { modulePath, methodName, isVoid, yieldResults, parameters = [], isDebug } = parsedData;

        // Validate modulePath
        if (!modulePath || typeof modulePath !== "string") {
            console.error("Invalid module path received.");
            return new Uint8Array(new TextEncoder().encode(JSON.stringify({ error: "Invalid module path." })));
        }

        // Dynamically import the module
        const targetModule = await getModule(modulePath);
        if (typeof targetModule[methodName] !== "function") {
            console.error(`Method '${methodName}' not found in ${modulePath}.`);
            return new Uint8Array(new TextEncoder().encode(JSON.stringify({ error: `Method '${methodName}' not found.` })));
        }

        let safeParameters = parameters.map(param => JSON.parse(param));

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
        let responseJson = JSON.stringify(result || {});
        let encodedResponse = new TextEncoder().encode(responseJson);
        return new Uint8Array(encodedResponse);
    } catch (error) {
        console.error("Error handling streamed JS:", error);
        return new Uint8Array(new TextEncoder().encode(JSON.stringify({ error: "Unexpected error in JS." })));
    }
}


// Utility function to split large JSON strings into 31KB chunks
function chunkString(str, size) {
    const chunks = [];
    for (let i = 0; i < str.length; i += size) {
        chunks.push(str.substring(i, i + size));
    }
    return chunks;
}