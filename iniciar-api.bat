@echo off
setlocal

rem ============================================================================
rem  pedidos-autos - ApiCore
rem
rem  Prende la base (iniciar-mysql.bat) y despues levanta la API en primer
rem  plano, en esta misma consola: los logs se ven aca. Ctrl+C la corta.
rem  Queda en http://127.0.0.1:5080 (probar /health).
rem ============================================================================

rem El .bat se escribe sin acentos a proposito: cmd lee el archivo con el
rem codepage vigente ANTES de este chcp, asi que un acento aca se rompe. El
rem chcp es para la salida de la API, que si los usa.
chcp 65001 >nul

rem Los appsettings se buscan en el DIRECTORIO DE TRABAJO, no al lado del
rem binario. Sin este cd, lanzarlo desde otra carpeta muere con "Falta la cadena
rem de conexion" aunque el archivo exista.
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [X] No se encontro 'dotnet'. Hace falta el .NET 10 SDK.
    pause
    exit /b 1
)

if not exist "appsettings.Local.json" (
    echo [X] Falta appsettings.Local.json con la cadena de conexion.
    echo     Copia appsettings.Local.example.json y completa la clave de pedidos_api
    echo     ^(esta en D:\MySQL\credenciales-3308.txt^).
    pause
    exit /b 1
)

rem La base primero: sin ella la API arranca igual, pero /health da 503 y no se
rem puede verificar el schema.
call "%~dp0iniciar-mysql.bat"
if errorlevel 1 exit /b 1

title pedidos-autos - ApiCore

dotnet run --project "%~dp0ApiCore.csproj" -- %*

set CODIGO=%errorlevel%

rem La API aborta con codigo 1 cuando a la base le falta una migracion. Sin este
rem pause, el doble clic cierra la ventana antes de que se pueda leer que falto.
if not "%CODIGO%"=="0" (
    echo.
    echo [X] Termino con codigo %CODIGO%. Lee el mensaje de arriba.
    pause
)

exit /b %CODIGO%
