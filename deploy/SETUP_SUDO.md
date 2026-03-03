# Настройка sudo без пароля на VM

Для работы deploy.bat нужно настроить sudo без пароля для пользователя `expmap` на сервере `192.168.66.154`.

## Вариант 1: Настроить sudo без пароля (рекомендуется)

На сервере выполните:

```bash
sudo visudo
```

Добавьте в конец файла:

```
expmap ALL=(ALL) NOPASSWD: ALL
```

Или более безопасный вариант (только для конкретных команд):

```
expmap ALL=(ALL) NOPASSWD: /usr/bin/git, /usr/bin/dotnet, /bin/systemctl, /bin/rm, /bin/mkdir
```

Сохраните и выйдите (Ctrl+X, Y, Enter).

## Вариант 2: Использовать SSH ключи

Если у вас настроены SSH ключи, можно выполнять команды без sudo там, где это возможно.

## Вариант 3: Изменить права на директорию проекта

```bash
sudo chown -R expmap:expmap /home/expmap/tunnelmanager
sudo chmod -R 755 /home/expmap/tunnelmanager
```

Это позволит работать с файлами без sudo.

## Проверка

После настройки проверьте:

```bash
ssh expmap@192.168.66.154 "sudo -n whoami"
```

Должно вывести `root` без запроса пароля.
