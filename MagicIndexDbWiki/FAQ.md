# FAQ
#### ❓ “Wait… does it actually avoid loading everything into memory first?”

Yes. And you’ve already addressed this wonderfully. But emphasize in your FAQ that **Magic only touches what it needs, when it needs it**—_with proof and yield-backed behavior, key cache yield prevention, and other magic as well_. 😉

#### ❓ “But can you do `||` conditions? I thought IndexedDB couldn’t.”

Why can't indexedDB do that? Because of API limitations? HA! I laugh in the face of limitations! Magic IndexedDB isn't just a library. it's a truth based intent query engine, which translates predicate based intent as the source of truth. Magic IndexedDB isn't just a library, it's a new protocol and philosophy. Hint hint, Magic IndexedDB is a pet project prototype before dropping a much larger version of this protocol. It's so much more than indexedDB.

#### ❓ “Why do I still need to care about indexes if Magic is so powerful?”

Because **index-based queries will always be faster than cursors**—no matter how smart you are. The cursor is powerful as it utilized a meta data cursor lookup system. With groundwork built for further optimizations to make what was thought as impossible to index, a possibility to index. Oh yea, there's some cool doors that've opened up here!

But no matter how much cool stuff you do. Just like any database, an index is always the most powerful and fast lookup you can perform.

#### ❓ “Can I use this in my framework/language?”

Which one? Which part? Pick and choose my friends because this is a modular, universal translation layer. You can build a wrapper around the engine pieces you want. And we can still share and upgrade the main engine together! Just because we're in separate languages doesn't mean we can't be friends.

#### ❓ “So… you’ve replaced Dexie?”

No. I actually use Dexie.JS as the backbone of this project. Dexie is amazing, it brings tons of reliability, a strong community, and fantastic wrapping capabilities around IndexedDB. This project pairs with Dexie, it doesn't replace.

#### ❓ “Can I do joins?”

IndexedDB is non relational so sadly no... **cough cough** maybe I should say, "not yet". Oh yea, there's some ideas that be brewing!

#### ❓ “Do I need System Reflections??”

Absolutely not. The universal layer handles everything with no reflections. It tracks the predicate, the universal language, all on your behalf. It's flexible yet strict. It's got your back, and you don't need to sweat a thing.

#### ❓ “But Nested && || Operations Are Allowed?”

Yup, for sure. And this isn't just any lazy flattening either. A system that can truly translate intent doesn't care. Your intent is translated, that's the truth and all that matters to the query engine. Focusing on intent based truth which is the predicate instead of thinking of things as only 1 query based API calls is what this project does differently.

#### ❓ “But but but but buttt”

Seriously, it's just magic. Embrace it, love it, and use it. If there's something you can't do, it's probably a bug or a feature that's incoming. I'm simply a priest that has performed an exorcism on all the demons within IndexedDB.