# Neo4j, not Stardog, for the knowledge graph

The client names Neo4j or Stardog. We use Neo4j 5 Community in Docker: it is free to self-host, has a mature .NET driver, and Cypher reads well in a code walkthrough. Stardog has no free self-hosted licence, so SPARQL/OWL reasoning is parked for industrialisation.

Aspire has no first-party Neo4j integration, so the AppHost runs it as a plain container (`neo4j:5-community`) with a generated password kept in user-secrets. Host ports are fixed at 7474/7687 so Neo4j Browser connects without configuration. The API does not connect to it until Stage 2.
