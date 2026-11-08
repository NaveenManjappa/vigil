# Graph primer: the Cypher Vigil uses

One page on the Neo4j ideas behind Vigil's graph, using Vigil's own data. Run the queries in Neo4j Browser at http://localhost:7474 after step 8 of the [user guide's walkthrough](user-guide.md#4-walk-through-margarets-takeover).

## Nodes and relationships

A **node** is a thing, written in round brackets with a label and properties: `(:Account {accountId: 'ACC-MARGARET'})`, `(:Device {value: 'D-77'})`. Every identifier node keeps its key in `value`, so a query can treat devices, emails and bank accounts alike.

A **relationship** joins two nodes, always has a direction and exactly one type, and can carry properties. Vigil's carry none: `(:Account)-[:USED_DEVICE]->(:Device)`. A Confirmed Fraud Account has the node property `confirmedFraud: true`. Vigil's relationship types are OWNS, USED_DEVICE, SEEN_FROM, HAS_EMAIL, HAS_PHONE, PAYS_TO and LIVES_AT.

## MATCH: find a pattern

`MATCH` describes a shape and Neo4j finds every place in the graph that has it. Variables (`a`, `r`, `d`) name the parts you want back.

```cypher
MATCH (a:Account {accountId: 'ACC-MARGARET'})-[r]->(id)
RETURN type(r), labels(id)[0], id.value
```

## Hops

A **hop** is one relationship crossed. Account to Device is one hop. Account to Device and back to another Account is two hops, and that is the shape the graph Rules G1 and G2 use: "which other Accounts share something with this one?"

```cypher
MATCH (a:Account {accountId: 'ACC-MARGARET'})-[:USED_DEVICE]->(d:Device)<-[:USED_DEVICE]-(other:Account)
RETURN d.value, other.accountId
```

The arrow on the right points back into the Device, because both Accounts point at it.

## WHERE: filter

`WHERE` narrows a match with conditions the pattern alone cannot say, such as "a different Holder" or "after a date".

```cypher
MATCH (h1:Holder)-[:OWNS]->(a:Account)-[:PAYS_TO]->(b:BankAccount)<-[:PAYS_TO]-(other:Account)<-[:OWNS]-(h2:Holder)
WHERE h1 <> h2
RETURN b.value, a.accountId, other.accountId
```

## count: aggregate

`count` collapses matching rows into a number per group. Every column that is not aggregated becomes the group key, as with SQL's GROUP BY, but there is no GROUP BY to write.

```cypher
MATCH (id)<-[:USED_DEVICE|PAYS_TO|HAS_EMAIL|HAS_PHONE]-(a:Account)
WITH id, count(a) AS accounts
WHERE accounts >= 2
RETURN labels(id)[0], id.value, accounts
ORDER BY accounts DESC
```

`WITH` passes results on to the next part of the query, which is how you filter on a count.

## MERGE: create unless it exists

`MERGE` finds the whole pattern or creates it, so running it twice leaves one copy. This is how the projector stays idempotent when the same Portal Event is posted again:

```cypher
MERGE (a:Account {accountId: 'ACC-MARGARET'})
MERGE (d:Device {value: 'D-77'})
MERGE (a)-[:USED_DEVICE]->(d)
```

Merge nodes first, then the relationship between them. `MERGE (a:Account {...})-[:USED_DEVICE]->(d:Device {...})` in one line would create a second D-77 if this Account had never used it, because MERGE matches the whole pattern or creates the whole pattern.

## Uniqueness constraints

`CREATE CONSTRAINT IF NOT EXISTS FOR (n:Device) REQUIRE n.value IS UNIQUE` makes Neo4j reject a second Device with the same value. The API creates one per node key at start. Two concurrent ingests could otherwise both find no D-77 and both create one. With the constraint, MERGE locks the key while it looks, so the second ingest waits and then finds the node the first one made. The constraint also gives MERGE an index to look the key up by.

```cypher
SHOW CONSTRAINTS
```

## Starting over

`MATCH (n) DETACH DELETE n` empties the graph (DETACH removes a node's relationships too). `POST /reset` runs it, and empties Postgres too.
