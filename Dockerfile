# syntax=docker/dockerfile:1
# =============================================================================
# Lesson 23-A — Multi-stage Dockerfile for the ASP.NET Core 10 banking app
#
# Why multi-stage?
#   • Build stage has the SDK (large); runtime stage has only the runtime (small).
#   • Final image is ~200 MB instead of ~900 MB.
#
# Java parallel:
#   Maven spring-boot:build-image / Jib                → dotnet publish
#   FROM eclipse-temurin:21 AS build → dotnet/sdk
#   FROM eclipse-temurin:21-jre AS runtime → dotnet/aspnet
# =============================================================================

# ── Stage 1: restore & build ──────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first to leverage layer caching for NuGet restore
COPY Lesson/Lesson.csproj              Lesson/
COPY Lesson.Tests/Lesson.Tests.csproj  Lesson.Tests/
RUN dotnet restore Lesson/Lesson.csproj

# Copy the rest of the source and publish in Release mode
COPY Lesson/   Lesson/
RUN dotnet publish Lesson/Lesson.csproj \
	--no-restore \
	-c Release \
	-o /app/publish \
	--os linux

# ── Stage 2: runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish .

# ASP.NET Core listens on 8080 by default in .NET 8+
EXPOSE 8080

# Docker HEALTHCHECK — probe the /health endpoint every 30 s
# Java parallel: HEALTHCHECK via Spring Boot Actuator /actuator/health
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Lesson.dll"]
