-- =============================================================================
-- 001 — Usuarios
--
-- Compradores, revendedores y administradores en una sola tabla: un usuario
-- tiene un rol, y el rol decide qué puede hacer. Correr con pedidos_migraciones.
-- =============================================================================

CREATE TABLE IF NOT EXISTS usuarios (
  id             BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT PRIMARY KEY,

  -- La API lo guarda ya normalizado (sin espacios, en minúscula). La collation
  -- _as_ci igual lo hace único sin importar mayúsculas: dos cuentas con
  -- Juan@x.com y juan@x.com serían la misma persona con dos contraseñas.
  -- Distingue acentos (_as_), porque para un email "é" y "e" no son lo mismo.
  -- 254 es el largo máximo de un email según la RFC 5321.
  email          VARCHAR(254)     CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_ci NOT NULL,

  -- Argon2id en formato PHC, con sal y parámetros adentro:
  --   $argon2id$v=19$m=19456,t=2,p=1$<sal>$<hash>
  -- Ronda los 100 caracteres; 255 deja lugar para subir el costo sin tocar esto.
  password_hash  VARCHAR(255)     NOT NULL,

  nombre         VARCHAR(80)      NOT NULL,

  -- WhatsApp en formato E.164 (+5491122334455). NULL hasta que lo cargue: el
  -- botón de contacto necesita el número, el registro no.
  telefono       VARCHAR(20)      NULL,

  rol            ENUM('comprador', 'revendedor', 'admin') NOT NULL,

  -- Suspendido = no entra y sus pedidos u ofertas no se muestran. Los datos se
  -- conservan para la moderación.
  estado         ENUM('activo', 'suspendido') NOT NULL DEFAULT 'activo',

  -- Todo en UTC, escrito por la API. La hora argentina se calcula al mostrar.
  creado         DATETIME(3)      NOT NULL,
  actualizado    DATETIME(3)      NOT NULL,

  UNIQUE KEY uq_usuarios_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
