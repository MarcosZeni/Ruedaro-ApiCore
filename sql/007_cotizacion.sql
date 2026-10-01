-- =============================================================================
-- 007 — Dólar oficial (BCRA) y precios en dólares
--
-- Correr con pedidos_migraciones.
--
-- La API toma la cotización del dólar de la API pública del BCRA y guarda una
-- fila por día. Cada pedido, oferta y aviso guarda su monto en dólares y la
-- cotización usada AL PUBLICARLO: así se puede mostrar "≈ USD 659 (dólar a
-- $1.517 al publicar)" y filtrar por precio comparando pesos con dólares.
--
-- La cotización se congela en la fila a propósito: que mañana suba el dólar no
-- cambia lo que el vendedor publicó ni lo que el comprador vio.
-- =============================================================================

CREATE TABLE IF NOT EXISTS cotizaciones (
  -- Fecha de la cotización según el BCRA (no la del momento en que la pedimos).
  fecha      DATE           NOT NULL PRIMARY KEY,
  -- Pesos por dólar.
  valor      DECIMAL(12,4)  NOT NULL,
  obtenida   DATETIME(3)    NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- NULL en los montos en dólares solo si al publicar no había ninguna
-- cotización disponible (BCRA caído y ninguna guardada): no frena la publicación.
ALTER TABLE pedidos
  ADD COLUMN presupuesto_usd BIGINT UNSIGNED NULL AFTER moneda,
  ADD COLUMN cotizacion      DECIMAL(12,4)   NULL AFTER presupuesto_usd;

ALTER TABLE ofertas
  ADD COLUMN precio_usd      BIGINT UNSIGNED NULL AFTER moneda,
  ADD COLUMN cotizacion      DECIMAL(12,4)   NULL AFTER precio_usd;

ALTER TABLE publicaciones
  ADD COLUMN precio_usd      BIGINT UNSIGNED NULL AFTER moneda,
  ADD COLUMN cotizacion      DECIMAL(12,4)   NULL AFTER precio_usd;
