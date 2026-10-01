-- =============================================================================
-- 004 — Pedidos
--
-- Lo que publica el comprador: qué busca y cuánto quiere gastar. Es el centro
-- del producto. Correr con pedidos_migraciones.
-- =============================================================================

CREATE TABLE IF NOT EXISTS pedidos (
  id             BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT PRIMARY KEY,
  comprador_id   BIGINT UNSIGNED  NOT NULL,

  -- Las listas válidas (tipos, marcas por tipo, zonas) viven en
  -- ApiCore/Catalogo.cs y Web/src/lib/catalogo.ts. La API valida contra ellas.
  tipo           ENUM('auto', 'moto', 'camion', 'lancha') NOT NULL,
  marca          VARCHAR(40)      NOT NULL,
  modelo         VARCHAR(80)      NOT NULL,
  anio_desde     SMALLINT UNSIGNED NOT NULL,
  anio_hasta     SMALLINT UNSIGNED NOT NULL,

  -- Entero: en autos usados nadie habla de centavos. BIGINT porque en pesos
  -- una lancha o un camión pasa de los 4.000 millones que entran en un INT.
  presupuesto    BIGINT UNSIGNED  NOT NULL,
  moneda         ENUM('USD', 'ARS') NOT NULL,
  zona           VARCHAR(40)      NOT NULL,
  comentarios    VARCHAR(500)     NULL,

  -- activo   lo ven los revendedores y recibe ofertas
  -- cerrado  lo cerró el comprador (motivo_cierre dice por qué)
  -- baja     lo bajó un administrador; no lo ve nadie más que el admin
  estado         ENUM('activo', 'cerrado', 'baja') NOT NULL DEFAULT 'activo',
  motivo_cierre  ENUM('encontrado', 'ya_no_busca') NULL,

  -- Todo en UTC, escrito por la API.
  creado         DATETIME(3)      NOT NULL,
  actualizado    DATETIME(3)      NOT NULL,

  -- El listado de los revendedores: activos, de un tipo, los más nuevos primero.
  INDEX idx_pedidos_listado (estado, tipo, creado),
  INDEX idx_pedidos_comprador (comprador_id, creado),

  CONSTRAINT fk_pedidos_comprador FOREIGN KEY (comprador_id) REFERENCES usuarios (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
