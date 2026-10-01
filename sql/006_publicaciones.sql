-- =============================================================================
-- 006 — Publicaciones (avisos del catálogo, pestaña "Buscar")
--
-- Lo que un revendedor tiene para vender. Correr con pedidos_migraciones.
-- =============================================================================

CREATE TABLE IF NOT EXISTS publicaciones (
  id             BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT PRIMARY KEY,
  revendedor_id  BIGINT UNSIGNED  NOT NULL,

  tipo           ENUM('auto', 'moto', 'camion', 'lancha') NOT NULL,
  marca          VARCHAR(40)      NOT NULL,
  modelo         VARCHAR(80)      NOT NULL,
  version        VARCHAR(80)      NOT NULL,
  anio           SMALLINT UNSIGNED NOT NULL,
  -- Kilómetros, u horas de motor si es una lancha.
  uso            INT UNSIGNED     NOT NULL,
  precio         BIGINT UNSIGNED  NOT NULL,
  moneda         ENUM('USD', 'ARS') NOT NULL,
  zona           VARCHAR(40)      NOT NULL,
  descripcion    VARCHAR(1000)    NULL,

  -- activa   se ve en el catálogo
  -- pausada  la pausó el revendedor (reservada, mostrándola); puede volver
  -- vendida  definitiva
  -- baja     la bajó un administrador
  estado         ENUM('activa', 'pausada', 'vendida', 'baja') NOT NULL DEFAULT 'activa',
  creado         DATETIME(3)      NOT NULL,
  actualizado    DATETIME(3)      NOT NULL,

  INDEX idx_publicaciones_listado (estado, tipo, creado),
  INDEX idx_publicaciones_revendedor (revendedor_id, creado),

  CONSTRAINT fk_publicaciones_revendedor FOREIGN KEY (revendedor_id) REFERENCES usuarios (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
