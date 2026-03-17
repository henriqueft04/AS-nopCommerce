.PHONY: all up down build run logs collector-logs clean

all: up build run

up:
	docker compose -f observability/docker-compose.yml up -d
	@echo "Waiting for MySQL to be ready..."
	@sleep 15

down:
	docker compose -f observability/docker-compose.yml down

build:
	cd src && dotnet restore && dotnet build --no-restore

run:
	@until bash -c 'echo > /dev/tcp/localhost/3306' 2>/dev/null; do echo "Waiting for MySQL..."; sleep 2; done
	@echo "MySQL is up"
	cd src && OTEL_DOTNET_AUTO_LOG_LEVEL=debug dotnet run --project Presentation/Nop.Web/Nop.Web.csproj

logs:
	docker compose -f observability/docker-compose.yml logs -f

collector-logs:
	docker compose -f observability/docker-compose.yml logs -f otel-collector

clean:
	docker compose -f observability/docker-compose.yml down -v
