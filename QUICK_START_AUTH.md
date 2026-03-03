# Быстрый старт: Basic Auth для сервисов

## Добавить защиту к сервису

```powershell
cd tunnelling
.\add-auth.ps1 -Domain "comfy1.denys.fast" -Username "admin" -Password "YourSecurePassword"
```

## Примеры

### Защитить ComfyUI
```powershell
.\add-auth.ps1 -Domain "comfy1.denys.fast" -Username "comfyuser" -Password "ComfyPass2024"
```

### Защитить VLLM
```powershell
.\add-auth.ps1 -Domain "vllm.denys.fast" -Username "vllmuser" -Password "VllmPass2024"
```

### Добавить второго пользователя
```powershell
.\update-auth.ps1 -Domain "comfy1.denys.fast" -Username "user2" -Password "Pass2"
```

### Посмотреть все защищенные домены
```powershell
.\list-auth.ps1
```

### Удалить защиту
```powershell
.\remove-auth.ps1 -Domain "comfy1.denys.fast"
```

## Что происходит

1. Создается файл паролей `/etc/nginx/.htpasswd_{domain}`
2. В Nginx конфигурацию добавляются директивы `auth_basic`
3. Nginx перезагружается
4. При доступе к домену браузер запросит логин/пароль

## Важно

- Пароли хранятся в зашифрованном виде (bcrypt)
- Basic Auth передает пароль в base64 (лучше чем ничего, но не идеально)
- Используйте сложные пароли
- Не используйте одинаковые пароли для разных сервисов
