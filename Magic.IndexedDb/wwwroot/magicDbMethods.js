"use strict";

import * as MagicDbModule from './magicDB.js';

// Console logging wrapper
function consoleLog(message, isDebug) {
    if (isDebug) {
        console.log(message);
    }
}

export async function streamedJsHandler(streamRef) {
    consoleLog("Received streamRef", true);

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

        let { methodName, isVoid, parameters = [], isDebug } = parsedData;
        consoleLog(`Parsed Data: ${JSON.stringify(parsedData)}`, isDebug);

        // Deserialize parameters
        let safeParameters = parameters.map(param => JSON.parse(param));
        consoleLog(`Fixed Parameters (Deserialized): ${JSON.stringify(safeParameters)}`, isDebug);
        parameters = null; // Free memory

        // Free parsedData reference
        parsedData = null;

        if (typeof MagicDbModule[methodName] === "function") {
            let result = await MagicDbModule[methodName](...safeParameters);
            safeParameters = null; // Free memory after function call

            if (isVoid) {
                consoleLog(`Void method '${methodName}' executed successfully.`, isDebug);
                return new Uint8Array(new TextEncoder().encode("true"));
            }

            // Stream response
            let responseJson = JSON.stringify(result || {});
            let encodedResponse = new TextEncoder().encode(responseJson);
            responseJson = null; // Free memory
            return new Uint8Array(encodedResponse);
        } else {
            console.error(`Method '${methodName}' not found in MagicDbModule.`);
            return new Uint8Array(new TextEncoder().encode(JSON.stringify({ error: `Method '${methodName}' not found.` })));
        }
    } catch (error) {
        console.error("Error handling streamed JS:", error);
        return new Uint8Array(new TextEncoder().encode(JSON.stringify({ error: "Unexpected error in JS." })));
    }
}
