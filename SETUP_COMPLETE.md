# ✅ WireGuard Tunnel - Настройка завершена!

## Статус: ТУННЕЛЬ РАБОТАЕТ

### Выполнено

1. ✅ **WireGuard Server (DO VM)** - настроен и запущен
   - IP: 10.0.0.1/24
   - Порт: 51820/udp
   - Peer добавлен: `mNt8GuBdwt5N+Z8QY0r4+4zE5WSCpAasenjc5xT1YFo=`

2. ✅ **WireGuard Client (LXC)** - настроен и запущен
   - IP: 10.0.0.2/24
   - IP forwarding включен
   - NAT правила настроены

3. ✅ **Cloudflare Tunnel** - остановлен и отключен

4. ✅ **Nginx Reverse Proxy** - настроен для всех 12 сервисов:
   - n8n.denys.fast → 192.168.66.150:5678
   - gpu.denys.fast → 192.168.66.142:5000
   - apigencomf.denys.fast → 192.168.66.142:8188
   - simuqlator.denys.fast → 192.168.66.154:80
   - tg.denys.fast → 192.168.66.154:5000
   - llm.denys.fast → 192.168.66.154:3000
   - interfaceblob.denys.fast → 192.168.66.154:6001
   - comfy1.denys.fast → 192.168.66.142:8187
   - vllm.denys.fast → 192.168.66.142:8000
   - server1.denys.fast → 192.168.66.141:8006
   - server1gpu.denys.fast → 192.168.66.142:5001
   - SSH tunnel: порт 2222 → 192.168.66.154:22

5. ✅ **Проверка туннеля**:
   - DO VM → LXC (10.0.0.2): ✅ Работает
   - DO VM → Внутренние IP (192.168.66.x): ✅ Работает
   - Сервисы доступны через туннель: ✅ Работает

6. ✅ **PowerShell скрипты** - созданы и готовы к использованию

---

## ⚠️ Что нужно сделать ВАМ

### 1. Настроить DNS в Cloudflare

**ВАЖНО:** Выполните это сейчас, чтобы сервисы были доступны по доменам.

1. Зайдите в Cloudflare Dashboard для домена `denys.fast`
2. Удалите старые CNAME записи Cloudflare Tunnel
3. Создайте A-записи (Proxy OFF - серое облако):

| Имя | Тип | Значение | Proxy |
|-----|-----|----------|-------|
| n8n | A | 159.223.76.215 | **OFF** |
| gpu | A | 159.223.76.215 | **OFF** |
| apigencomf | A | 159.223.76.215 | **OFF** |
| simulationserverssh | A | 159.223.76.215 | **OFF** |
| simuqlator | A | 159.223.76.215 | **OFF** |
| tg | A | 159.223.76.215 | **OFF** |
| llm | A | 159.223.76.215 | **OFF** |
| interfaceblob | A | 159.223.76.215 | **OFF** |
| comfy1 | A | 159.223.76.215 | **OFF** |
| vllm | A | 159.223.76.215 | **OFF** |
| server1 | A | 159.223.76.215 | **OFF** |
| server1gpu | A | 159.223.76.215 | **OFF** |

**Proxy должен быть OFF (серое облако)**, иначе SSL от Let's Encrypt не сработает!

### 2. Получить SSL сертификаты

После обновления DNS (подождите 1-5 минут), выполните на DO VM:

```bash
certbot --nginx -d n8n.denys.fast -d gpu.denys.fast -d apigencomf.denys.fast -d simuqlator.denys.fast -d tg.denys.fast -d llm.denys.fast -d interfaceblob.denys.fast -d comfy1.denys.fast -d vllm.denys.fast -d server1.denys.fast -d server1gpu.denys.fast --non-interactive --agree-tos --email admin@denys.fast
```

Или используйте PowerShell скрипт для каждого домена:

```powershell
.\add-forward.ps1 -Domain "n8n.denys.fast" -Target "192.168.66.150:5678" -GetSSL
```

---

## 🧪 Тестирование

После настройки DNS и SSL, проверьте доступность:

```bash
# HTTP (до SSL)
curl -I http://gpu.denys.fast
curl -I http://n8n.denys.fast

# HTTPS (после SSL)
curl -I https://gpu.denys.fast
curl -I https://n8n.denys.fast
```

---

## 📋 Использование PowerShell скриптов

Все скрипты находятся в `tunnelling/`:

```powershell
# Просмотр всех пробросов
.\list-forwards.ps1

# Добавить новый проброс
.\add-forward.ps1 -Domain "new.denys.fast" -Target "192.168.66.142:8080"

# Удалить проброс
.\remove-forward.ps1 -Domain "old.denys.fast"

# Изменить target
.\edit-forward.ps1 -Domain "service.denys.fast" -Target "192.168.66.142:9000"

# Статус туннеля
.\status.ps1
```

---

## 🔧 Устранение проблем

### Если туннель не работает

```bash
# На DO VM
wg show
systemctl status wg-quick@wg0

# На LXC
wg show
systemctl status wg-quick@wg0
```

### Если внутренние IP не доступны

```bash
# На LXC - проверить маршруты
ip route show
sysctl net.ipv4.ip_forward

# Проверить iptables правила
iptables -t nat -L -n
```

### Если Nginx не проксирует

```bash
# На DO VM
nginx -t
tail -f /var/log/nginx/error.log
```

---

## 📊 Архитектура

```
Internet → DO VM (159.223.76.215) → WireGuard → LXC (192.168.66.151) → Внутренние серверы
                Nginx :80/:443         10.0.0.1/24    10.0.0.2/24        192.168.66.x
```

---

## ✅ Готово к использованию!

После настройки DNS и SSL все сервисы будут доступны по доменам через быстрый WireGuard туннель вместо медленного Cloudflare Tunnel.
