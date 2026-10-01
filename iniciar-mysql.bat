@echo off
setlocal

rem ============================================================================
rem  pedidos-autos - Prender la base de datos
rem
rem  Levanta el servicio MySQL8Autos: instancia propia de MySQL 8 en
rem  127.0.0.1:3308, aparte de las del juego (3307) y Tower of Argentum (46000).
rem  Configuracion en D:\MySQL\my-3308.ini, datos en D:\MySQL\data3308 y
rem  claves en D:\MySQL\credenciales-3308.txt (fuera del repo).
rem
rem  Si el servicio no existe, lo instala (una sola vez, con arranque Manual:
rem  no se prende solo al encender la maquina). Para que si lo haga, en una
rem  consola de administrador:
rem      Set-Service MySQL8Autos -StartupType Automatic
rem ============================================================================

rem El .bat se escribe sin acentos a proposito: cmd lee el archivo con el
rem codepage vigente ANTES de este chcp, asi que un acento aca se rompe.
chcp 65001 >nul

set SERVICIO=MySQL8Autos
set PUERTO=3308
set INI=D:\MySQL\my-3308.ini
set MYSQLD=C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqld.exe

rem Si ya esta corriendo no hay nada que hacer, y sobre todo no hay que pedir
rem un UAC al pedo: el caso normal es abrir esto y que la base ya este viva.
sc query "%SERVICIO%" | find "RUNNING" >nul
if not errorlevel 1 (
    echo [OK] %SERVICIO% ya estaba corriendo.
    goto :verificar
)

sc query "%SERVICIO%" >nul 2>nul
if not errorlevel 1 goto :arrancar

rem ---- El servicio no existe: se instala ------------------------------------
if not exist "%INI%" (
    echo [X] No existe %INI%. Sin la configuracion no se puede instalar el servicio.
    pause
    exit /b 1
)
if not exist "%MYSQLD%" (
    echo [X] No se encontro mysqld.exe en:
    echo     %MYSQLD%
    pause
    exit /b 1
)

echo Instalando el servicio %SERVICIO% (una sola vez^). Acepta el aviso de Windows.
rem Instalar y arrancar en la misma elevacion: un solo UAC.
powershell -NoProfile -Command "$c = '& ''%MYSQLD%'' --install-manual %SERVICIO% --defaults-file=%INI%; Start-Service %SERVICIO%'; Start-Process -Verb RunAs -Wait -FilePath powershell -ArgumentList '-NoProfile','-Command',$c"
goto :verificar

:arrancar
echo Arrancando %SERVICIO%...

rem Primero sin elevar: si esta consola ya es de administrador, no aparece UAC.
net start "%SERVICIO%" >nul 2>nul
if not errorlevel 1 goto :verificar

rem Sin permisos: se eleva SOLO el arranque del servicio. -Wait para que el
rem chequeo de abajo no corra antes de que MySQL termine de levantar.
echo Hacen falta permisos de administrador. Acepta el aviso de Windows.
powershell -NoProfile -Command "Start-Process -Verb RunAs -Wait -FilePath 'net' -ArgumentList 'start','%SERVICIO%'"

:verificar
rem Que el servicio diga RUNNING no garantiza que ya acepte conexiones: mysqld
rem tarda un momento en abrir el socket. Se chequea el puerto, que es lo que a
rem la API le importa.
powershell -NoProfile -Command "$ok=$false; foreach($i in 1..20){ if((Test-NetConnection 127.0.0.1 -Port %PUERTO% -InformationLevel Quiet -WarningAction SilentlyContinue)){$ok=$true;break}; Start-Sleep -Milliseconds 700 }; if($ok){exit 0}else{exit 1}"

if errorlevel 1 (
    echo.
    echo [X] El puerto %PUERTO% no responde. La base no esta aceptando conexiones.
    echo     Mira el log de error en D:\MySQL\data3308 ^(archivo .err^).
    pause
    exit /b 1
)

echo [OK] MySQL escuchando en 127.0.0.1:%PUERTO%.
exit /b 0
