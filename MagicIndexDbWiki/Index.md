This is the start of the Magic IndexDB wiki


![]! Don't forget to edit the JS side to not specifically need a where statement.

Large Refactor notes:
[] Removed the "Count" capability and moved it to the Enumerable execution until it's properly supported

[] Removed, "ResultsNotUnique" and instead I need to replace that with .Unique

[] If there is no where statement or additional query additions, we simply execute a get all

[] I've added support for first and last


[] We now support nested OR operations

[] After flattening we should be making it more optimized and removing redundancies

[] Remove documentation for ToList and AsEnumerable, these concepts must be rebuilt because syncrounous just isn't a viable option within Blazor due to the fundamental implementation of async invoke.

[] Added support for true asyncrounous yielding! Though beware the cons of large amounts of serializations and deserializations

[] Am I supporting Take, TakeLast, skip, orderby, all that on just the query itself?

[] Users should know they may need to re-order the results on return when I'm not doing so for them.

[] Unique complex queries need to rerun, "Unique"

[] Validate yielding still works!
[] Add cursor and more index tests

[] Validate that when yielding back from cursor we remove what we can, but if it's a skip or take we must do that at the very end. Maybe we should also get the results and then go back in to grab things?

[] Handling multiple chained Ordering happens in memory, so we should prevent that with the the interfaces!
	- Like we should have a Cursor stage level

![]! Add not contains support!

[] Remove warnings

[] Can we say if ID === what was already found that we skip it in the validation?


[] Things like, "TakeLast" or "Take" we should be as efficient as possible. Like replacing in memory what's found for example and replacing it when a better one following X operations occurs, you know?

[] Need specialized logic like double cursor queries to get ordering material first, get all's after, build what's necessary in memory and don't pull it all! LIke if there's a skip, take, or take last, this is when we can't truly yield the results but must do memory magic to perform similarly.


[] Now support Not operations like Not greater than, `!(x._Age > 40)` and now supported is NotContains

[] Add next finalized layer! Anything past means a cursor layer!

[] Remove the opening of databases 

[] Build unit tests with all paths to validate they index right

[] Document how skip take works

[] Record complex take, skip, etc operations and how fallback to cursor works.

[] Document  more than 1 indexed queries is more than 1 indexed query but we have a take, skip, or last, then we do all predicate!

[] Removed unecessary json serializations and deserializations that were from legacy level code.

![]! Take skip should be reversed on behalf of users for the cursor! This is super important! 

[] Dynamic objects even with placed magic attributes won't be respected.

[] I could make easy validation as well if desired

[] Should I add early compound key support?

[] Never allow Skip after take or take last to follow IndexDB query logic to replicate full index search queries for stability and normalization of syntax.

[] Cursor mode activated!

[] Document how complex queries happen in a memory performant way.

[] Compound key support added

[] On startup validation

[] How to build your repository system

[] Self validation to keep you safe

[] Document that you now must specify primary key if it increments or not.
	- We could also auto build in for users auto incrementing Guid's or other if they wanted on C#'s side.
### Showcase
new setup to add wasm, signalR or override and service setup

[] Document wwwroot migration system with server based wwwroot for Hybrid Blazor.

[] Handles column additions, consistent nullable, and more.
	- Tracks cursor found order to keep database row order logic consistent with take, skip, take last, etc grabs along with indexed queries. 

[] Added "Count"
# Must DO
[] Describe translated intent
[] Validate updates, deletes, and the other methods

[] Setup the AutoIncrement to work on db store!

[] Import Profile
[] Exit setting change
[] Allow multiple net frameworks into compilation!

[] Allow bypassing assembly check if they know it's been ran.

[] Interfaces that protect you

# Potential
[] We could also auto build in for users auto incrementing Guid's or other if they wanted on C#'s side.

# To Do
[] Validate all instances being utilized in indexDB is asyncrounous
[] Add open database
[] add close database
[] Should I increase the yield amount above 32 KB? 

#### NEW self documenting Universal Language Model
orConditionsArray is now NestedOrFilter
```json
{
  "orGroups": [
    {
      "andGroups": [
        {
          "conditions": [
            { "property": "Name", "operation": "StartsWith", "value": "j", "isString": true, "caseSensitive": false },
            { "property": "Age", "operation": "GreaterThan", "value": 35, "isString": false, "caseSensitive": false }
          ]
        },
        {
          "conditions": [
            { "property": "Name", "operation": "Contains", "value": "bo", "isString": true, "caseSensitive": false }
          ]
        },
        {
          "conditions": [
            { "property": "Name", "operation": "StartsWith", "value": "c", "isString": true, "caseSensitive": false }
          ]
        },
        {
          "conditions": [
            { "property": "Name", "operation": "StartsWith", "value": "l", "isString": true, "caseSensitive": false }
          ]
        }
      ]
    }
  ]
}

```



OLD WAY OF CODE orConditionsArray:
```json
[
[
  [
    { "property": "Name", "operation": "StartsWith", "value": "j" },
    { "property": "Age", "operation": "GreaterThan", "value": 35 }
  ],
  [
    { "property": "Name", "operation": "Contains", "value": "bo" }
  ],
  [
    { "property": "Name", "operation": "StartsWith", "value": "c" }
  ],
  [
    { "property": "Name", "operation": "StartsWith", "value": "l" }
  ]
]
]
```

```cs
/// <summary>
/// Return a list of items in which the items do not have to be unique. Therefore, you can get 
/// duplicate instances of an object depending on how you write your query.
/// </summary>
/// <param name="amount"></param>
/// <returns></returns>
public MagicQuery<T> ResultsNotUnique()
{
    ResultsUnique = false;
    return this;
}
```


Never added but will add:

```cs
 private MagicQuery<T> First()
 {
     StoredMagicQuery smq = new StoredMagicQuery();
     smq.Name = MagicQueryFunctions.First;
     storedMagicQueries.Add(smq);
     return this;
 }

 // Not yet working
 private MagicQuery<T> Last()
 {
     StoredMagicQuery smq = new StoredMagicQuery();
     smq.Name = MagicQueryFunctions.Last;
     storedMagicQueries.Add(smq);
     return this;
 }
```
