import { legacyWrite } from './cutover.js';
import { addItem } from './_content/Magic.IndexedDb/magicDB.js';

self.onmessage = async ({ data }) => {
    try {
        await legacyWrite(() => addItem({ dbName: 'fixture-west', storeName: 'invoices',
            record: { invoice_id: data, Account: 'bridge-worker', Sequence: 1 } }));
        self.postMessage('written');
    } catch (error) { self.postMessage(error.message); }
};
self.postMessage('ready');
