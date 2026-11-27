# portfolio-backend

Colección de proyectos de backend y datos. Cada carpeta es un proyecto independiente, con sus propios tests y README.

## Proyectos

| Proyecto | Descripción |
|---|---|
| [api-users](api-users/) | API REST de usuarios con autenticación JWT (access + refresh token), roles y SQL Server. |
| [api-services-kafka](api-services-kafka/) | Dos microservicios (pedidos e inventario) que se comunican por Kafka, cada uno con su base de datos PostgreSQL. Outbox transaccional y consumidores idempotentes. |
| [dbt](dbt/) | Proyecto de introducción a dbt (dbt Core y PostgreSQL) con datos de ejemplo: modelos de staging y marts, un modelo incremental, tests de datos y documentación con linaje. |

`api-users` y `api-services-kafka` están hechos en .NET 10; `dbt` usa SQL y Python.
