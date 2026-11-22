# portfolio-backend

Colección de proyectos backend en .NET 10. Cada carpeta es un proyecto independiente, con su propia solución, tests y README.

## Proyectos

| Proyecto | Descripción |
|---|---|
| [api-users](api-users/) | API REST de usuarios con autenticación JWT (access + refresh token), roles y SQL Server. |
| [api-services-kafka](api-services-kafka/) | Dos microservicios (pedidos e inventario) que se comunican por Kafka, cada uno con su base de datos PostgreSQL. Outbox transaccional y consumidores idempotentes. |
