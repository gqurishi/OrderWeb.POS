# Data-transfer objects

DTOs here define the host-neutral data required by shared screens and the
Mother/Client boundary. Do not move Mother database entities or Client SQLite
row models into this directory. Host adapters map their existing domain, API,
and cache models to these contracts.
