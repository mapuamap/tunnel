# Docker Deployment для Tunnel Manager

## Структура

- `Dockerfile` - образ для сборки приложения
- `docker-compose.prod.yml` - конфигурация Docker Compose для продакшена
- `deploy/deploy.bat` - скрипт автоматического деплоя

## Настройка

### 1. Установить GIT_TOKEN

```powershell
$env:GIT_TOKEN = "your_github_token"
```

Или установить постоянно (см. `SETUP_GIT_TOKEN.md`)

### 2. Настроить путь на VM

По умолчанию проект будет деплоиться в:
```
/home/expmap/docker/opt/stacks/tunnelmanager
```

Убедитесь, что директория существует на VM:
```bash
ssh expmap@192.168.66.154
mkdir -p /home/expmap/docker/opt/stacks/tunnelmanager
```

## Использование

### Запуск деплоя

```powershell
.\deploy\deploy.bat
```

### Что делает скрипт

1. **Инкремент версии** в appsettings.json
2. **Сборка проекта** (Release)
3. **Git add** всех изменений
4. **Git commit** с автоматическим сообщением
5. **Git push** на GitHub
6. **Деплой на VM**:
   - Подключение по SSH
   - `git fetch` и `git reset --hard` (обновление кода)
   - `docker compose down` (остановка контейнера)
   - `docker compose up -d --build` (сборка и запуск)

## Порт

Приложение работает на порту **5101** (внутри контейнера и снаружи).

## Проверка после деплоя

```bash
ssh expmap@192.168.66.154
cd /home/expmap/docker/opt/stacks/tunnelmanager
docker compose ps
docker compose logs -f tunnelmanager
```

## Ручной деплой (если скрипт не работает)

```bash
ssh expmap@192.168.66.154
cd /home/expmap/docker/opt/stacks/tunnelmanager
git fetch https://GIT_TOKEN@github.com/mapuamap/tunnel.git master
git reset --hard FETCH_HEAD
docker compose -f docker-compose.prod.yml down
docker compose -f docker-compose.prod.yml up -d --build
```

## Troubleshooting

### Ошибка "GIT_TOKEN not set"
Установите переменную окружения (см. выше)

### Ошибка при push на GitHub
GitHub может блокировать push из-за секретов в истории. Скрипт продолжит деплой даже если push не удался.

### Ошибка при деплое на VM
Проверьте:
- SSH доступ к VM
- Наличие Docker и Docker Compose на VM
- Права на директорию проекта
- Наличие GIT_TOKEN в переменных окружения
