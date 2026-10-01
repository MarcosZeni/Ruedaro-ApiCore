-- =============================================================================
-- 008 — Patente y destacados
--
-- Correr con pedidos_migraciones.
-- =============================================================================

-- Plan de destacados del usuario. Gratis: 1 aviso destacado. Con el plan
-- vigente (plan_vence en el futuro): hasta 10. Lo activa un admin a mano
-- mientras no haya cobro integrado.
ALTER TABLE usuarios
  ADD COLUMN plan_vence DATETIME(3) NULL AFTER estado;

ALTER TABLE publicaciones
  -- Patente (o matrícula, en lanchas), normalizada: mayúsculas, sin espacios ni
  -- guiones. NO se muestra al público: con la patente se pueden clonar papeles.
  -- La ven el dueño del aviso y los administradores.
  ADD COLUMN patente         VARCHAR(12) NOT NULL DEFAULT '' AFTER zona,

  -- Destacado = aparece en el carrusel. destacada_desde ordena los destacados de
  -- cada usuario: si se le vence el plan, queda en el carrusel solo el primero.
  ADD COLUMN destacada       TINYINT(1)  NOT NULL DEFAULT 0 AFTER estado,
  ADD COLUMN destacada_desde DATETIME(3) NULL AFTER destacada,

  -- Una patente no puede estar en dos avisos vigentes a la vez (evita el spam
  -- del mismo vehículo y que alguien publique uno ajeno). MySQL no tiene índices
  -- únicos parciales: esta columna vale la patente mientras el aviso está
  -- activo o pausado, y NULL cuando terminó (vendido, dado de baja). El índice
  -- único ignora los NULL, así que la misma patente se puede volver a publicar
  -- después de vender.
  ADD COLUMN patente_vigente VARCHAR(12) AS (
    CASE WHEN estado IN ('activa', 'pausada') AND patente <> '' THEN patente END
  ) STORED,
  ADD UNIQUE KEY uq_publicaciones_patente_vigente (patente_vigente),

  -- El carrusel: destacados activos de un tipo de vendedor.
  ADD INDEX idx_publicaciones_destacadas (destacada, estado);
