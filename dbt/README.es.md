# Introducción a dbt

[English version](README.md)

Un proyecto local y pequeño para aprender qué es dbt y cómo funciona. Usa datos de una tienda ficticia.

## Qué hace dbt

dbt (data build tool) transforma datos que **ya están dentro** de una base de datos. Escribes consultas `select`, y dbt crea las vistas y tablas en el orden correcto. Además prueba los datos y genera documentación.

dbt no extrae datos de otros sistemas ni programa tareas.

| Concepto | Significado | Ejemplo en este proyecto |
|---|---|---|
| Seed | Un CSV pequeño que dbt carga en una tabla | [seeds/raw_orders.csv](seeds/raw_orders.csv) |
| Modelo | Un archivo `.sql` con un `select`. Se convierte en vista o tabla | [stg_orders.sql](models/staging/stg_orders.sql) |
| `ref()` | Una referencia a otro modelo o seed. dbt la usa para saber el orden | [dim_customers.sql](models/marts/dim_customers.sql) |
| Materialización | Cómo guarda dbt un modelo: vista, tabla o incremental | [dbt_project.yml](dbt_project.yml), [fct_orders.sql](models/marts/fct_orders.sql) |
| Test de datos | Una comprobación. Falla cuando su consulta devuelve filas | [_staging.yml](models/staging/_staging.yml), [assert_no_negative_payments.sql](tests/assert_no_negative_payments.sql) |
| Linaje | Un diagrama que muestra qué modelo depende de cuál | `dbt docs` (ver abajo) |

## Flujo del proyecto

```
seeds (CSV)  →  staging (vistas: limpiar y renombrar)  →  marts (tablas finales)
```

| Seed | Staging | Marts |
|---|---|---|
| `raw_customers` | `stg_customers` | `dim_customers` (tabla) |
| `raw_orders` | `stg_orders` | `dim_customers`, `fct_orders` (incremental) |
| `raw_payments` | `stg_payments` | `dim_customers`, `fct_orders` |

Todo se crea en el esquema `analytics`.

## Requisitos

- Docker Desktop, abierto
- Python 3.14 (la versión con la que se probó el proyecto)

## Puesta en marcha

Ejecuta estos comandos en PowerShell, dentro de la carpeta `dbt`.

```powershell
py -3.14 -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
docker compose up -d --wait
dbt debug
```

Si PowerShell no permite ejecutar el script de activación, usa `.\.venv\Scripts\dbt.exe` en lugar de `dbt`.

`dbt debug` debe terminar con `All checks passed!`. La conexión está en [profiles.yml](profiles.yml) y solo tiene credenciales locales de prueba.

Puede que dbt muestre un aviso sobre dbt v2. Este proyecto usa dbt Core v1, con las versiones de [requirements.txt](requirements.txt).

## Comandos principales

| Comando | Qué hace |
|---|---|
| `dbt seed` | Carga los CSV |
| `dbt run` | Crea los modelos |
| `dbt test` | Ejecuta los tests de datos |
| `dbt build` | Hace todo lo anterior en orden de dependencias. Si falla un test, omite los modelos que dependen de esos datos |
| `dbt run --select +fct_orders` | Ejecuta `fct_orders` y todo lo que necesita |
| `dbt show --select dim_customers` | Muestra algunas filas de un modelo |
| `dbt compile` | Convierte el Jinja en SQL puro |

Para empezar:

```powershell
dbt seed
dbt build
```

La última línea debe mostrar `ERROR=0` y `TOTAL=35`: 3 seeds, 5 modelos y 27 tests.

**Ver el SQL real:** después de un build, abre `target/compiled/jaffle_intro/models/marts/fct_orders.sql`. Verás que dbt reemplazó `ref()` por los nombres reales de las tablas.

## Documentación y linaje

```powershell
dbt docs generate
dbt docs serve --port 8090
```

Abre <http://127.0.0.1:8090>. El botón de abajo a la derecha muestra el diagrama de linaje. `Ctrl+C` detiene el servidor.

## El modelo incremental

`fct_orders` no reconstruye toda la tabla cada vez.

- **Primera ejecución o `--full-refresh`:** carga todos los pedidos.
- **Ejecuciones siguientes:** solo lee los pedidos con un `updated_at` posterior al más reciente de la tabla. Después reemplaza las filas con el mismo `order_id`.

Este diseño tiene límites:

- Si se agrega o corrige un pago y no cambia el `updated_at` del pedido, `fct_orders.amount` conserva el valor anterior.
- Si un pedido cambia, pero su nuevo `updated_at` es igual o anterior al más reciente de la tabla, el cambio se ignora.
- Si un pedido se elimina del origen, su fila sigue en `fct_orders`.

En esos casos, ejecuta `dbt build --full-refresh`.

`dim_customers` es una tabla normal, así que dbt la reconstruye completa cada vez. Por eso los dos marts pueden mostrar totales distintos hasta que hagas un full refresh.

## Ejercicio 1: hacer fallar un test

1. En `seeds/raw_orders.csv`, cambia el estado del pedido `1` de `returned` a `lost`.
2. Ejecuta `dbt build`.
3. Falla el test `accepted_values` de `stg_orders.status`. dbt **omite** los modelos que dependen de `stg_orders`, así que el valor erróneo no llega a los marts.
4. Vuelve a poner `returned` y ejecuta `dbt build`. Debe mostrar `ERROR=0`.

Mientras exista el error, la vista de staging sigue mostrando la fila errónea. Una vista siempre lee los datos actuales.

## Ejercicio 2: ver cómo funciona el modelo incremental

1. En `seeds/raw_orders.csv`, cambia el pedido `99` a `completed` y su `updated_at` a `2018-04-10 00:00:00`:

   ```
   99,85,2018-04-09,completed,2018-04-10 00:00:00
   ```

2. Agrega esta línea al final del mismo archivo:

   ```
   100,1,2018-04-10,placed,2018-04-10 00:00:00
   ```

3. Agrega esta línea al final de `seeds/raw_payments.csv`:

   ```
   114,100,credit_card,2500
   ```

4. Ejecuta `dbt build`. En la línea de `fct_orders`, `INSERT 0 2` significa que solo se escribieron dos filas.
5. Revisa el resultado:

   ```powershell
   dbt show --inline "select * from {{ ref('fct_orders') }} where order_id >= 99 order by order_id"
   dbt show --inline "select count(*) as orders from {{ ref('fct_orders') }}"
   ```

   El pedido `99` está `completed`, el pedido `100` tiene un importe de `25` y hay 100 pedidos.

**Restaurar:** deshaz los tres cambios en los CSV y ejecuta `dbt build --full-refresh`. Un build normal no borra el pedido `100` de `fct_orders`. Después del full refresh vuelve a haber 99 pedidos.

## Problemas frecuentes

| Problema | Qué hacer |
|---|---|
| `failed to connect to the docker API` | Abre Docker Desktop y espera a que esté listo |
| `port is already allocated` | Otro programa usa el puerto 5435. Deténlo, o cambia el puerto en `docker-compose.yml` y `profiles.yml` |
| `connection refused` en `dbt debug` | Ejecuta `docker compose up -d --wait` |
| Un test o modelo nuevo no se ejecuta | Ejecuta `dbt build --no-partial-parse` |

Sobre la última fila: dbt guarda una caché de los archivos del proyecto. Al construir este proyecto escribimos los tests antes que los modelos. dbt los deshabilitó con una advertencia, y siguieron deshabilitados después de crear los modelos. El build terminó con `ERROR=0` sin ejecutar ningún test. Lee las advertencias y revisa el número de `TOTAL`.

## Apagar la base

```powershell
docker compose down      # conserva los datos
docker compose down -v   # borra los datos
```

## Origen de los datos

Los CSV vienen de [dbt-labs/jaffle-shop-classic](https://github.com/dbt-labs/jaffle-shop-classic), commit `fd7bfacae4f497ff044a6a0275268676bf1b64c3`, con licencia Apache 2.0 (ver [LICENSE-jaffle-shop-classic](LICENSE-jaffle-shop-classic)). Ese repositorio está archivado.

**Cambio realizado:** `raw_orders.csv` tiene una columna nueva, `updated_at`, con la fecha del pedido a medianoche. La necesita el modelo incremental. Los modelos y tests de esta carpeta se escribieron para este proyecto.

El origen guarda los importes sin decimales. Este proyecto los interpreta como centavos y los divide por 100.
