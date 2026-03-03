# Настройка GIT_TOKEN для deploy.bat

## Временная установка (для текущей сессии PowerShell)

```powershell
$env:GIT_TOKEN = "YOUR_GITHUB_TOKEN_HERE"
```

## Постоянная установка (для всех сессий)

### Вариант 1: Через PowerShell профиль

1. Откройте PowerShell
2. Выполните:
```powershell
notepad $PROFILE
```

3. Добавьте строку:
```powershell
$env:GIT_TOKEN = "YOUR_GITHUB_TOKEN_HERE"
```

4. Сохраните и перезапустите PowerShell

### Вариант 2: Через системные переменные окружения Windows

1. Откройте "Система" → "Дополнительные параметры системы"
2. Нажмите "Переменные среды"
3. В разделе "Переменные пользователя" нажмите "Создать"
4. Имя: `GIT_TOKEN`
5. Значение: `YOUR_GITHUB_TOKEN_HERE`
6. Нажмите OK и перезапустите терминал

### Вариант 3: Через командную строку (CMD)

```cmd
setx GIT_TOKEN "YOUR_GITHUB_TOKEN_HERE"
```

После этого перезапустите командную строку.

## Проверка

```powershell
echo $env:GIT_TOKEN
```

Должен вывести ваш токен.
