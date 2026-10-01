-- =============================================================================
-- 005 — Ofertas
--
-- La respuesta de un revendedor a un pedido. Correr con pedidos_migraciones.
-- =============================================================================

CREATE TABLE IF NOT EXISTS ofertas (
  id             BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT PRIMARY KEY,
  pedido_id      BIGINT UNSIGNED  NOT NULL,
  revendedor_id  BIGINT UNSIGNED  NOT NULL,

  -- Puede ofrecer otro modelo parecido al pedido, así que lleva sus propios datos.
  marca          VARCHAR(40)      NOT NULL,
  modelo         VARCHAR(80)      NOT NULL,
  version        VARCHAR(80)      NOT NULL,
  anio           SMALLINT UNSIGNED NOT NULL,
  -- Kilómetros, u horas de motor si el pedido es de una lancha.
  uso            INT UNSIGNED     NOT NULL,
  precio         BIGINT UNSIGNED  NOT NULL,
  moneda         ENUM('USD', 'ARS') NOT NULL,
  link           VARCHAR(500)     NULL,
  comentario     VARCHAR(500)     NULL,

  -- baja: la bajó un administrador; el comprador deja de verla.
  estado         ENUM('visible', 'baja') NOT NULL DEFAULT 'visible',
  creado         DATETIME(3)      NOT NULL,

  -- La regla "un revendedor oferta una sola vez por pedido" la garantiza la
  -- base, no la pantalla: dos clics rápidos no pueden crear dos ofertas.
  UNIQUE KEY uq_ofertas_pedido_revendedor (pedido_id, revendedor_id),
  INDEX idx_ofertas_revendedor (revendedor_id, creado),

  CONSTRAINT fk_ofertas_pedido FOREIGN KEY (pedido_id) REFERENCES pedidos (id),
  CONSTRAINT fk_ofertas_revendedor FOREIGN KEY (revendedor_id) REFERENCES usuarios (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
