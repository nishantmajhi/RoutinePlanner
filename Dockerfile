# syntax=docker/dockerfile:1

# ---- Build stage -----------------------------------------------------------
# Only the Web project is built here (not the test project) — tests aren't part
# of the deployed image.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the csproj first and restore, so `docker build` can cache the restore
# layer and skip it on rebuilds that only touch .cs files.
COPY src/RoutinePlanner.Web/RoutinePlanner.Web.csproj src/RoutinePlanner.Web/
RUN dotnet restore src/RoutinePlanner.Web/RoutinePlanner.Web.csproj

# Now copy the rest of the project (controllers, views, App_Data seed files, etc.)
# and publish a self-contained Release build.
COPY src/RoutinePlanner.Web/ src/RoutinePlanner.Web/
RUN dotnet publish src/RoutinePlanner.Web/RoutinePlanner.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ---- Runtime stage ----------------------------------------------------------
# The ASP.NET runtime image (not the SDK) — no compiler/build tools shipped,
# much smaller than the build stage.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
# Render injects $PORT at runtime and routes traffic to whatever port the app
# binds to (see the PORT handling in Program.cs) — 8080 here is just the
# documented default for local `docker run`, not a hard requirement.
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "RoutinePlanner.Web.dll"]
