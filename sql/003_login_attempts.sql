-- =============================================================================
-- 003 — Intentos de login y registro
--
-- Una fila por intento, salga bien o mal. Correr con pedidos_migraciones.
--
-- Para qué:
--   - Frenos que sobreviven a un reinicio de la API: intentos por IP por minuto
--     y contraseñas equivocadas seguidas contra un mismo email.
--   - Investigar: "¿desde dónde entraron a esta cuenta?".
--
-- La API purga lo más viejo que la retención configurada (90 días).
-- =============================================================================

CREATE TABLE IF NOT EXISTS login_attempts (
  id          BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT PRIMARY KEY,
  fecha       DATETIME(3)      NOT NULL,
  ip          VARBINARY(16)    NOT NULL,

  -- Lo que escribió, ya normalizado. Puede no corresponder a ninguna cuenta.
  email       VARCHAR(254)     CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_ci NULL,

  -- NULL = ese email no existe (no que el intento haya fallado).
  usuario_id  BIGINT UNSIGNED  NULL,

  -- ok                ingresó
  -- clave_incorrecta  el email existe y la contraseña no coincide
  -- email_inexistente nadie con ese email
  -- suspendido        credenciales bien, cuenta suspendida
  -- frenado           cortado por demasiados intentos (no cuenta para el freno)
  -- registro          cuenta creada (cuenta para el freno por IP)
  resultado   ENUM('ok', 'clave_incorrecta', 'email_inexistente', 'suspendido', 'frenado', 'registro') NOT NULL,

  -- ISO 3166 (AR, US) resuelto offline con MaxMind al insertar; 'ZZ' si no se
  -- supo. Se congela acá: actualizar la base de MaxMind no reescribe el pasado.
  pais        CHAR(2)          NOT NULL,

  INDEX idx_login_attempts_ip_fecha (ip, fecha),
  INDEX idx_login_attempts_email_fecha (email, fecha),
  INDEX idx_login_attempts_fecha (fecha)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
