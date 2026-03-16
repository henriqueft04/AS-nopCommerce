.PHONY: all up down build run logs clean

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
	cd src && dotnet run --project Presentation/Nop.Web/Nop.Web.csproj

logs:
	docker compose -f observability/docker-compose.yml logs -f

clean:
	docker compose -f observability/docker-compose.yml down -v
