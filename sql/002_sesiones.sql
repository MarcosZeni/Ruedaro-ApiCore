-- =============================================================================
-- 002 — Sesiones
--
-- Una fila por sesión iniciada. Correr con pedidos_migraciones.
--
-- El token viaja en una cookie httpOnly de la web y llega a la API en el header
-- Authorization. Acá se guarda solo su SHA-256: quien lea esta tabla no puede
-- usar ninguna sesión.
--
-- Cerrar sesión = borrar la fila. Suspender a un usuario corta todas sus
-- sesiones en el acto, porque cada pedido mira usuarios.estado.
-- =============================================================================

CREATE TABLE IF NOT EXISTS sesiones (
  token_hash   BINARY(32)       NOT NULL PRIMARY KEY,
  usuario_id   BIGINT UNSIGNED  NOT NULL,

  -- Desde dónde se inició: sirve para mostrarle al usuario sus sesiones y para
  -- investigar un robo de cuenta. VARBINARY(16) entra IPv4 e IPv6.
  ip           VARBINARY(16)    NOT NULL,

  -- Todo en UTC.
  creado       DATETIME(3)      NOT NULL,
  expira       DATETIME(3)      NOT NULL,

  INDEX idx_sesiones_usuario (usuario_id),
  INDEX idx_sesiones_expira (expira),

  -- ON DELETE CASCADE: si algún día se borra un usuario, sus sesiones se van con él.
  CONSTRAINT fk_sesiones_usuario FOREIGN KEY (usuario_id) REFERENCES usuarios (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
