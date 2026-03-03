# Basic HTTP Authentication для Nginx

Набор скриптов для добавления базовой HTTP аутентификации (Basic Auth) к сервисам, которые не имеют собственной аутентификации.

## Возможности

- ✅ Автоматическое добавление Basic Auth к существующим доменам
- ✅ Создание файлов паролей через `htpasswd`
- ✅ Удаление аутентификации
- ✅ Просмотр списка защищенных доменов
- ✅ Автоматическая установка `apache2-utils` (если не установлен)

## Использование

### Добавить аутентификацию

```powershell
.\add-auth.ps1 -Domain "example.denys.fast" -Username "admin" -Password "secure_password" -Realm "Private Area"
```

**Параметры:**
- `-Domain` - домен, к которому добавляется аутентификация (обязательно)
- `-Username` - имя пользователя (обязательно)
- `-Password` - пароль (обязательно)
- `-Realm` - название области аутентификации (опционально, по умолчанию "Restricted Access")

**Пример:**
```powershell
.\add-auth.ps1 -Domain "comfy1.denys.fast" -Username "user1" -Password "MySecurePass123"
```

### Обновить пароль или добавить пользователя

```powershell
.\update-auth.ps1 -Domain "example.denys.fast" -Username "admin" -Password "new_password"
```

Этот скрипт обновит пароль существующего пользователя или добавит нового пользователя к существующей аутентификации.

### Удалить аутентификацию

```powershell
.\remove-auth.ps1 -Domain "example.denys.fast"
```

### Просмотреть защищенные домены

```powershell
.\list-auth.ps1
```

Выведет список всех доменов с настроенной Basic Auth, включая realm и статус файла паролей.

## Как это работает

1. **Создание файла паролей:**
   - Используется `htpasswd` для создания файла `/etc/nginx/.htpasswd_{domain}`
   - Пароли хранятся в зашифрованном виде (bcrypt)

2. **Модификация Nginx конфигурации:**
   - В блок `location /` добавляются директивы:
     ```nginx
     auth_basic "Restricted Access";
     auth_basic_user_file /etc/nginx/.htpasswd_example.denys.fast;
     ```

3. **Проверка и перезагрузка:**
   - Проверяется корректность конфигурации (`nginx -t`)
   - Перезагружается Nginx (`systemctl reload nginx`)

## Примеры использования

### Защитить ComfyUI без аутентификации

```powershell
# Добавить защиту
.\add-auth.ps1 -Domain "comfy1.denys.fast" -Username "comfyuser" -Password "ComfyPass2024"

# Проверить список
.\list-auth.ps1

# Удалить защиту (если нужно)
.\remove-auth.ps1 -Domain "comfy1.denys.fast"
```

### Защитить несколько сервисов

```powershell
# ComfyUI
.\add-auth.ps1 -Domain "comfy1.denys.fast" -Username "admin" -Password "pass1"

# VLLM
.\add-auth.ps1 -Domain "vllm.denys.fast" -Username "admin" -Password "pass2"

# API Gen Comfy
.\add-auth.ps1 -Domain "apigencomf.denys.fast" -Username "admin" -Password "pass3"
```

## Безопасность

⚠️ **Важно:**
- Пароли передаются через командную строку (видны в истории)
- Используйте сложные пароли
- Регулярно меняйте пароли
- Не используйте одинаковые пароли для разных сервисов
- Basic Auth передает пароль в base64 (не полностью безопасно, но лучше чем ничего)

## Добавление нескольких пользователей

Для добавления нескольких пользователей к одному домену, используйте SSH и команду `htpasswd`:

```bash
# На сервере (159.223.76.215)
htpasswd /etc/nginx/.htpasswd_example.denys.fast username2
# Введите пароль при запросе
```

## Устранение неполадок

### Ошибка: "htpasswd not found"
Скрипт автоматически установит `apache2-utils`, но если это не сработало:
```bash
apt-get update && apt-get install -y apache2-utils
```

### Ошибка: "Configuration file not found"
Убедитесь, что домен был создан через `add-forward.ps1` или существует конфигурация в `/etc/nginx/sites-available/`

### Аутентификация не работает
1. Проверьте конфигурацию: `nginx -t`
2. Проверьте логи: `tail -f /var/log/nginx/error.log`
3. Убедитесь, что файл паролей существует: `ls -la /etc/nginx/.htpasswd_*`

## Файлы

- `_add_auth.py` - Python скрипт для добавления аутентификации
- `add-auth.ps1` - PowerShell скрипт для добавления (основной)
- `update-auth.ps1` - PowerShell скрипт для обновления пароля/добавления пользователя
- `remove-auth.ps1` - PowerShell скрипт для удаления
- `list-auth.ps1` - PowerShell скрипт для просмотра списка
