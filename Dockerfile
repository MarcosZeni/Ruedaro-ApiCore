# Build con el SDK completo; la imagen final es la "chiseled" de Ubuntu: sin
# shell, sin gestor de paquetes y con usuario no root por defecto. Si alguien
# logra ejecutar algo adentro, no tiene con qué seguir. La variante -extra trae
# ICU y tzdata, para que las zonas horarias IANA (America/Argentina/Buenos_Aires)
# existan dentro del contenedor.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero solo el csproj: la restauración queda en cache mientras no cambien
# las dependencias.
COPY ApiCore.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish ApiCore.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app ./

# Dentro del contenedor tiene que escuchar en todas las interfaces de SU red;
# que no quede expuesta a internet lo decide el docker-compose (sin "ports").
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ApiCore.dll"]
