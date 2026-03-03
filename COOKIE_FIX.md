# Исправление проблемы с сессиями/cookies

## Проблема

После настройки WireGuard туннеля пользователи не могли оставаться авторизованными - их выкидывало на страницу логина через 1-2 секунды после входа.

## Причина

При работе через HTTPS reverse proxy (Nginx), приложение получало запросы как HTTP (внутренне), но браузер отправлял их через HTTPS. Это приводило к проблемам с cookies:

1. Cookie устанавливалась с `Secure=false` (потому что приложение видело HTTP запрос)
2. Браузер отправлял запросы через HTTPS
3. Браузер не передавал не-Secure cookie обратно через HTTPS
4. Сессия терялась

## Решение

Обновлены все конфигурации Nginx для правильной работы cookies через reverse proxy.

### Добавленные заголовки:

```nginx
# CRITICAL: Force HTTPS for cookies
proxy_set_header X-Forwarded-Proto https;
proxy_set_header X-Forwarded-Scheme https;
proxy_set_header X-Forwarded-Host $host;
proxy_set_header X-Forwarded-Port 443;

# Cookie handling
proxy_cookie_path / /;
```

### Что это делает:

1. **X-Forwarded-Proto: https** - Принудительно сообщает приложению, что запрос пришел через HTTPS
2. **X-Forwarded-Scheme: https** - Дополнительный заголовок для некоторых приложений
3. **X-Forwarded-Host** - Передает правильный hostname
4. **X-Forwarded-Port: 443** - Указывает HTTPS порт
5. **proxy_cookie_path / /** - Обеспечивает правильный путь для cookies

## Обновленные конфигурации

Все 11 доменов обновлены:
- ✅ n8n.denys.fast
- ✅ gpu.denys.fast
- ✅ apigencomf.denys.fast
- ✅ simuqlator.denys.fast
- ✅ tg.denys.fast
- ✅ llm.denys.fast
- ✅ interfaceblob.denys.fast
- ✅ comfy1.denys.fast
- ✅ vllm.denys.fast
- ✅ server1.denys.fast
- ✅ server1gpu.denys.fast

## Результат

Теперь приложения правильно определяют, что запросы приходят через HTTPS, и устанавливают Secure cookies, которые работают корректно через браузер.

**Проблема решена на стороне туннеля (Nginx) - изменения в приложениях не требуются!**

## Проверка

После этих изменений:
1. Очистите cookies в браузере для доменов *.denys.fast
2. Войдите в систему заново
3. Сессия должна сохраняться правильно
