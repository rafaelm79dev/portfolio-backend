with source as (
    select * from {{ ref('raw_payments') }}
)

select
    id as payment_id,
    order_id,
    payment_method,
    -- the source stores cents
    (amount / 100.0)::numeric(10, 2) as amount
from source
