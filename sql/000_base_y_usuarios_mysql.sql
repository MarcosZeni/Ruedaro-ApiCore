-- =============================================================================
-- 000 — Base de datos y usuarios de MySQL
--
-- Se corre UNA vez por servidor, como root. Los demás scripts se corren con el
-- usuario de migraciones creado acá.
--
-- Dos usuarios, a propósito:
--
--   pedidos_api          el que usa la API. Solo lee y escribe datos. Si alguien
--                        toma control de la API, no puede borrar tablas, cambiar
--                        el schema ni tocar otras bases.
--
--   pedidos_migraciones  el que corre los scripts de sql/. Puede crear y alterar
--                        tablas, pero solo en esta base y solo desde localhost
--                        (adentro del contenedor o del servidor de MySQL): no se
--                        puede usar desde la red.
--
-- Las contraseñas las genera MySQL (IDENTIFIED BY RANDOM PASSWORD) y aparecen
-- UNA sola vez, en el resultado de este script. Copiarlas en ese momento:
-- la de pedidos_api va a ConnectionStrings__Default de la API. Así no queda una
-- contraseña de ejemplo escrita en un archivo esperando que alguien la use.
--
-- Si el usuario ya existe, CREATE USER IF NOT EXISTS no hace nada y no muestra
-- contraseña. Para regenerarla:
--   ALTER USER 'pedidos_api'@'%' IDENTIFIED BY RANDOM PASSWORD;
-- =============================================================================

CREATE DATABASE IF NOT EXISTS pedidos_autos
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;

-- '%' porque la API se conecta desde otro contenedor, con una IP que cambia.
-- MySQL no se expone a internet: solo escucha en la red interna de Docker.
CREATE USER IF NOT EXISTS 'pedidos_api'@'%' IDENTIFIED BY RANDOM PASSWORD;
GRANT SELECT, INSERT, UPDATE, DELETE ON pedidos_autos.* TO 'pedidos_api'@'%';

CREATE USER IF NOT EXISTS 'pedidos_migraciones'@'localhost' IDENTIFIED BY RANDOM PASSWORD;
GRANT CREATE, ALTER, DROP, INDEX, REFERENCES, SELECT, INSERT, UPDATE, DELETE
  ON pedidos_autos.* TO 'pedidos_migraciones'@'localhost';
