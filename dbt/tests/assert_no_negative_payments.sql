-- A singular test passes when the query returns no rows.
select *
from {{ ref('stg_payments') }}
where amount < 0
