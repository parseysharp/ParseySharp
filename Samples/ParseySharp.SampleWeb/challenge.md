# Validation Kata: Checkout

Implement two endpoints and validate according to the rules below. Return all validation errors (do not stop at the first failure).

## Endpoints

- POST `/checkout`
  - Accepts a single CheckoutRequest object
- POST `/checkout-history` (multipart + NDJSON)
  - Accepts a multipart/form-data request with a file field `history` of type `application/x-ndjson` (one CheckoutRequest per line)

## CheckoutRequest

- customerEmail: string
- paymentMethod: object with a discriminator `type: "card" | "ach"`
  - card → { number: string, cvv: int }
  - ach → { routingNumber: string, accountNumber: string }
- items: array of item
  - item.sku: string
  - item.quantity: int
  - item.unitPrice: decimal
- shippingAddress? object
  - shippingAddress.line1: string
  - shippingAddress.city: string
  - shippingAddress.country: string
  - shippingAddress.postal: string

Computed value for rules

- computedTotal = sum over items of (quantity × unitPrice)

## Validation rules

- Email: non-empty, trimmed, contains `@` and a domain
- Payment method conditionals
  - If type = card: cvv required (3–4 digits); number passes Luhn
  - If type = ach: routingNumber passes ABA checksum (9 digits); accountNumber non-empty
- Item-level
  - sku non-empty; quantity > 0; unitPrice > 0
- Cross-field
  - Card: infer brand from number; if Amex → cvv length = 4 else 3
  - If computedTotal ≥ 100.00: shippingAddress required (AVS)
  - If type = ach: computedTotal ≤ 25000.00 (cap)

## Sample inputs

### 1) Valid (Card, small amount)

```json
{
  "customerEmail": "alice@example.com",
  "paymentMethod": {
    "type": "card",
    "number": "4111111111111111",
    "cvv": 123
  },
  "items": [{ "sku": "A-100", "quantity": 2, "unitPrice": 10.0 }],
  "shippingAddress": {
    "line1": "1 Main St",
    "city": "Springfield",
    "country": "US",
    "postal": "90210"
  }
}
```

### 2) Invalid (Card, AVS threshold via computedTotal)

shippingAddress is omitted to trigger the AVS rule.

```json
{
  "customerEmail": "bob@example.com",
  "paymentMethod": {
    "type": "card",
    "number": "4111111111111111",
    "cvv": 123
  },
  "items": [{ "sku": "B-200", "quantity": 1, "unitPrice": 150.0 }]
}
```

### 3) Invalid (ACH over cap via computedTotal)

```json
{
  "customerEmail": "carol@example.com",
  "paymentMethod": {
    "type": "ach",
    "routingNumber": "021000021",
    "accountNumber": "123456789"
  },
  "items": [{ "sku": "C-300", "quantity": 1, "unitPrice": 26000.0 }],
  "shippingAddress": {
    "line1": "2 Main St",
    "city": "Springfield",
    "country": "US",
    "postal": "10001"
  }
}
```

### 4) Invalid (Card, Luhn fails)

```json
{
  "customerEmail": "dave@example.com",
  "paymentMethod": {
    "type": "card",
    "number": "4111111111111112",
    "cvv": 123
  },
  "items": [{ "sku": "D-400", "quantity": 1, "unitPrice": 10.0 }],
  "shippingAddress": {
    "line1": "3 Main St",
    "city": "Springfield",
    "country": "US",
    "postal": "30301"
  }
}
```

### 5) Invalid (ACH bad routing number)

```json
{
  "customerEmail": "dave@example.com",
  "paymentMethod": {
    "type": "ach",
    "routingNumber": "021000022",
    "accountNumber": "987654321"
  },
  "items": [{ "sku": "D-400", "quantity": 1, "unitPrice": 10.0 }],
  "shippingAddress": {
    "line1": "3 Main St",
    "city": "Springfield",
    "country": "US",
    "postal": "30301"
  }
}
```

## Deliverables

- Implement both endpoints:
  - POST `/checkout` (JSON)
  - POST `/checkout-history` (multipart/form-data) with a file field `history` of type `application/x-ndjson` (one CheckoutRequest per line)
- Validate the provided sample cases

## Success responses

- `/checkout`

  - 200 OK
  - Body: `{ "accepted": true, "paymentMethod": { "type": "card", "last4": "1111" } | { "type": "ach", "routingLast4": "0021", "accountLast4": "6789" }, "itemsCount": <int>, "amount": <decimal> }`

- `/checkout-history`
  - 200 OK
  - Body: `{ "accepted": true, "total": <int>, "byMethod": { "card": <int>, "ach": <int> }, "sumAmount": <decimal> }`

## Notes

- ABA routing checksum (9 digits): `3(d1 + d4 + d7) + 7(d2 + d5 + d8) + (d3 + d6 + d9) % 10 == 0`
- Simple brand inference by prefix: Amex starts 34/37; Visa 4; MasterCard 51–55 (demo-only)

### Sample NDJSON files for `/checkout-history`

- `checkout-history.valid.ndjson`

  - 3 valid rows. Expected outcome: 200 OK with
    - `accepted: true`
    - `total: 3`
    - `byMethod`: counts for `card` (2) and `ach` (1)
    - `sumAmount`: 145.0

- `checkout-history.invalid.ndjson`
  - 3 rows with scattered errors:
    - Row 1: bad email, failing Luhn, bad CVV length, empty SKU/quantity/price, empty address
    - Row 2: ACH routing checksum fails, empty account number, computedTotal over ACH cap
    - Row 3: Amex CVV length incorrect (should be 4), computedTotal ≥ 100 without address
  - Expected outcome: 400 with a list of validation errors; each error should include its row index in the path.

### Appendix: NDJSON and multipart example for `/checkout-history`

NDJSON (each line is a CheckoutRequest):

```
{"customerEmail":"alice@example.com","paymentMethod":{"type":"card","number":"4111111111111111","cvv":123},"items":[{"sku":"A-100","quantity":2,"unitPrice":10.0}]}
{"customerEmail":"bob@example.com","paymentMethod":{"type":"ach","routingNumber":"021000021","accountNumber":"123456789"},"items":[{"sku":"B-200","quantity":1,"unitPrice":5.0}]}
```
