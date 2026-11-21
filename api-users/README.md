# api-users

## Resumen rápido

| | |
|---|---|
| **Qué hace** | Registra usuarios, les permite iniciar sesión y devuelve tokens JWT. También renueva y cierra sesiones, y tiene un endpoint de administración. |
| **Cómo está armado** | .NET 10 con Minimal APIs y Clean Architecture en 4 proyectos: `Domain`, `Application`, `Infrastructure` y `Api`. |
| **Qué implementa** | Login con JWT (15 min) y refresh token de un solo uso (7 días) con detección de robo · roles `User` y `Admin` · bloqueo de cuenta tras 5 intentos fallidos · listado paginado de usuarios · límite de peticiones en `/auth` · documentación con OpenAPI/Scalar · 99 tests de integración. |
| **Base de datos** | **SQL Server** o **PostgreSQL** (incluye Supabase), según `Database:Provider`. |
| **Cómo probarlo** | Necesitas .NET 10 y Docker. Sigue la sección [Puesta en marcha](#puesta-en-marcha-con-sql-server): son cuatro pasos. |

A continuación está el detalle completo.

---

API REST en .NET 10 para registrar usuarios y autenticarlos con JWT. Incluye refresh tokens de un solo uso con detección de reutilización, roles (`User` y `Admin`) y persistencia en **SQL Server** o **PostgreSQL** a elección por configuración (Supabase incluido, porque es PostgreSQL).

## Características

- Registro, login, renovación de sesión (`refresh`) y cierre de sesión (`logout`).
- Access token JWT de 15 minutos y refresh token de 7 días.
- Los refresh tokens se guardan **solo como hash SHA-256**. Cada uno se usa una única vez: al renovarse, el anterior queda invalidado, y si alguien presenta uno ya usado se revoca toda la cadena de sesión (señal de robo). La rotación es atómica, así que dos peticiones simultáneas con el mismo token no pueden tener éxito a la vez.
- Contraseñas con ASP.NET Core Identity, bloqueo de cuenta tras 5 intentos fallidos (5 minutos) y una única respuesta `401` para email desconocido, contraseña errónea o cuenta bloqueada.
- Autorización por roles. El administrador inicial se siembra desde la configuración, nunca desde el código.
- Documentación interactiva con OpenAPI y Scalar (solo en desarrollo).
- Tests de integración contra una base de datos real en contenedor, ejecutables con ambos motores.

## Arquitectura

Clean Architecture en cuatro proyectos, con las dependencias apuntando hacia adentro:

```
Api ──► Application ──► Domain
 │           ▲
 └─► Infrastructure
 └─► Migrations.SqlServer / Migrations.PostgreSql ──► Infrastructure
```

| Proyecto | Responsabilidad |
|---|---|
| `ApiUsers.Domain` | Entidad `RefreshToken` con sus reglas (`IsActive`, `Revoke`) y `Roles`. Sin dependencias. |
| `ApiUsers.Application` | Casos de uso (`AuthService`, `UserService`), DTOs y los puertos (`IIdentityService`, `ITokenGenerator`, `IRefreshTokenRepository`). |
| `ApiUsers.Infrastructure` | EF Core, ASP.NET Core Identity, generación de JWT, repositorio de tokens y sembrado. Implementa los puertos. |
| `ApiUsers.Migrations.SqlServer` / `.PostgreSql` | Migraciones de EF Core, un juego por motor porque el SQL difiere. |
| `ApiUsers.Api` | Endpoints (Minimal APIs), autenticación JWT y OpenAPI. Solo traduce HTTP. |

`Application` no conoce `UserManager` ni EF Core, e `Infrastructure` no conoce a `Api` ni a los proyectos de migraciones. Unos tests de arquitectura lo verifican.

## Requisitos

- [SDK de .NET 10](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (para la base de datos y para los tests)

## Puesta en marcha con SQL Server

Desde la carpeta `api-users`:

```bash
# 1. Contraseñas de Docker (el archivo .env no se versiona)
cp .env.example .env          # en Windows: copy .env.example .env
# edita .env y completa MSSQL_SA_PASSWORD y POSTGRES_PASSWORD

# 2. Base de datos
docker compose up -d

# 3. Secretos de la API (se guardan fuera del repositorio)
cd src/ApiUsers.Api
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=ApiUsers;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True"
dotnet user-secrets set "Jwt:SigningKey" "<clave aleatoria de al menos 32 caracteres>"
dotnet user-secrets set "Admin:Email" "admin@example.com"
dotnet user-secrets set "Admin:Password" "<contraseña segura>"

# 4. Ejecutar
dotnet run
```

En desarrollo la API aplica las migraciones y siembra los roles y el administrador al arrancar, y abre la documentación en `http://localhost:5299/scalar/v1`.

Para generar una clave de firma: `openssl rand -base64 48`.

## Usar PostgreSQL o Supabase

La base de datos se elige con `Database:Provider` (`SqlServer` por defecto o `PostgreSql`). Un valor desconocido impide que la API arranque.

**PostgreSQL local**

```bash
docker compose --profile postgres up -d postgres
cd src/ApiUsers.Api
dotnet user-secrets set "Database:Provider" "PostgreSql"
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=apiusers;Username=postgres;Password=<POSTGRES_PASSWORD>"
```

**Supabase** (PostgreSQL alojado): usa la cadena de conexión de tu proyecto, con SSL obligatorio:

```bash
dotnet user-secrets set "Database:Provider" "PostgreSql"
dotnet user-secrets set "ConnectionStrings:Default" "Host=<host de Supabase>;Port=5432;Database=postgres;Username=<usuario>;Password=<contraseña>;SSL Mode=Require;Trust Server Certificate=true"
```

El host y el usuario dependen de si usas la conexión directa o el pooler; cópialos desde el panel de Supabase.

## Configuración

| Clave | Descripción | Por defecto |
|---|---|---|
| `Database:Provider` | `SqlServer` o `PostgreSql` | `SqlServer` |
| `Database:MigrateOnStartup` | Aplica migraciones y siembra roles/admin al arrancar | `false` (`true` en Development) |
| `ConnectionStrings:Default` | Cadena de conexión | — (obligatoria) |
| `Jwt:SigningKey` | Clave HS256, mínimo 32 bytes | — (obligatoria) |
| `Jwt:Issuer` / `Jwt:Audience` | Emisor y audiencia de los tokens | `api-users` / `api-users-clients` |
| `Jwt:AccessTokenMinutes` | Vida del access token | `15` |
| `Jwt:RefreshTokenDays` | Vida del refresh token | `7` |
| `Admin:Email` / `Admin:Password` | Administrador inicial (se omite si falta alguno) | — |
| `RateLimiting:AuthPermitLimit` | Peticiones permitidas a `/auth/*` por IP en cada ventana | `20` |
| `RateLimiting:AuthWindowSeconds` | Duración de la ventana, en segundos | `60` |

La API valida `Jwt` y `Database:Provider` al arrancar y falla con un mensaje claro si algo está mal. Las claves obligatorias nunca se guardan en el repositorio: usa `dotnet user-secrets` en desarrollo y variables de entorno (`Jwt__SigningKey`, ...) en otros entornos.

Con `Database:MigrateOnStartup` en `false` la API no toca el esquema: aplica las migraciones y crea los roles por tu cuenta antes de usarla.

### Seguridad y despliegue

- **Contraseñas:** mínimo 10 caracteres, con mayúscula, minúscula, dígito y símbolo.
- **Límite de peticiones:** `/auth/*` responde `429` con `Retry-After` al superar el límite por IP. Detrás de un proxy inverso, reenvía la IP real del cliente (`ForwardedHeaders`); si no, todos los clientes compartirán la cuota del proxy.
- **HTTPS:** fuera de Development la API activa HSTS y redirige HTTP a HTTPS. Si un proxy termina TLS, reenvía también el esquema original para evitar bucles de redirección.
- **Hosts permitidos:** `AllowedHosts` viene en `*`; en producción, limítalo a tu dominio.
- **Roles y sesiones:** el refresh rechaza cuentas bloqueadas, y `GET /users` comprueba en la base que quien llama sigue siendo `Admin`, sin esperar a que venza su access token.

## Endpoints

| Método | Ruta | Acceso | Descripción |
|---|---|---|---|
| POST | `/auth/register` | público | Crea un usuario con rol `User` |
| POST | `/auth/login` | público | Devuelve access token y refresh token |
| POST | `/auth/refresh` | público (con refresh token) | Rota el refresh token y emite un par nuevo |
| POST | `/auth/logout` | autenticado | Revoca la sesión del refresh token indicado |
| GET | `/users/me` | autenticado | Datos del usuario actual |
| GET | `/users?page=1&pageSize=20` | rol `Admin` | Lista paginada de usuarios (máximo 100 por página) |
| GET | `/health` | público | Comprobación de vida |

Ejemplo de uso:

```bash
curl -X POST http://localhost:5299/auth/register -H "Content-Type: application/json" \
  -d '{"email":"ana@example.com","password":"Passw0rd!x"}'

curl -X POST http://localhost:5299/auth/login -H "Content-Type: application/json" \
  -d '{"email":"ana@example.com","password":"Passw0rd!x"}'
# -> { "accessToken": "...", "refreshToken": "...", ... }

curl http://localhost:5299/users/me -H "Authorization: Bearer <accessToken>"

curl -X POST http://localhost:5299/auth/refresh -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refreshToken>"}'
```

Los errores usan el formato estándar `ProblemDetails`.

## Tests

Los tests son de integración y arrancan la API real contra una base de datos temporal en un contenedor, por lo que necesitan Docker.

```bash
dotnet test                                   # SQL Server
TEST_DB_PROVIDER=PostgreSql dotnet test       # PostgreSQL (en PowerShell: $env:TEST_DB_PROVIDER="PostgreSql")
```

## Migraciones

Cada motor tiene su propio proyecto de migraciones. Para agregar una, desde `api-users`:

```bash
dotnet tool restore

# SQL Server
Database__Provider=SqlServer ConnectionStrings__Default="Server=localhost;Database=x;User Id=sa;Password=x;TrustServerCertificate=True" \
  dotnet ef migrations add <Nombre> -p src/ApiUsers.Migrations.SqlServer -s src/ApiUsers.Api -o Migrations

# PostgreSQL
Database__Provider=PostgreSql ConnectionStrings__Default="Host=localhost;Database=x;Username=x;Password=x" \
  dotnet ef migrations add <Nombre> -p src/ApiUsers.Migrations.PostgreSql -s src/ApiUsers.Api -o Migrations
```

## Limitaciones conocidas

Cosas que esta API no hace y que conviene resolver antes de exponerla a usuarios reales:

- **Sin confirmación de email.** Cualquiera puede registrar un email ajeno. Hace falta un servicio de envío de correos y exigir `EmailConfirmed` en el login.
- **El `409` del registro revela que un email ya existe.** Es inherente a responder con un conflicto; el login sí devuelve siempre el mismo `401`.
- **Los refresh tokens vencidos o revocados no se purgan,** así que la tabla crece. Se resuelve con una tarea periódica de limpieza.
- **Una sola clave de firma.** Rotarla invalida los access tokens vigentes; para rotarla sin cortes haría falta un `kid` y una ventana con dos claves.
- **Los cambios de rol o el borrado de una cuenta tardan hasta 15 minutos** en reflejarse en los endpoints que solo leen el token. `GET /users`, `GET /users/me` y el refresh sí consultan la base.

## Decisiones de diseño

- **Identity detrás de un puerto.** Identity resuelve contraseñas, bloqueo y roles, pero queda aislado en `Infrastructure`; los casos de uso no dependen de él.
- **Refresh tokens con familias.** Cada sesión comparte un identificador de familia; reutilizar un token ya consumido o perder una carrera de rotación revoca la familia entera.
- **Un access token no se puede revocar antes de vencer** (15 minutos). Es una característica propia de JWT; `logout` revoca el refresh token, y `/users/me` consulta la base para rechazar cuentas eliminadas.
- **Respuestas que no revelan información.** Login responde lo mismo ante email desconocido, contraseña errónea o cuenta bloqueada, y calcula un hash aunque el email no exista para igualar los tiempos.
- **Listados paginados** con tope, para que un endpoint de administración no devuelva una tabla entera.
- **Un juego de migraciones por motor**, en proyectos separados, para no mezclar el SQL de cada proveedor.
