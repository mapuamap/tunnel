# Tunnel Manager - WireGuard Tunnel Setup

Утилиты для управления WireGuard туннелем между LXC контейнером и Digital Ocean VPS.

## Текущий статус

- ✅ DO VM настроен (WireGuard server, Nginx, Firewall)
- ✅ Nginx конфигурации созданы для всех 12 сервисов
- ✅ PowerShell скрипты созданы
- ⚠️ LXC контейнер требует ручной настройки (проблема с SSH доступом)

## Настройка LXC контейнера (ручная)

Поскольку автоматический доступ к LXC не работает, выполните следующие команды **на LXC контейнере (192.168.66.151)**:

### 1. Остановить Cloudflare Tunnel

```bash
# Если запущен как сервис
systemctl stop cloudflared
systemctl disable cloudflared

# Или если через Docker
docker stop cloudflared
docker rm cloudflared
```

### 2. Установить WireGuard

```bash
apt update
apt install -y wireguard wireguard-tools
```

### 3. Сгенерировать ключи

```bash
wg genkey | tee /etc/wireguard/client_private.key | wg pubkey > /etc/wireguard/client_public.key
chmod 600 /etc/wireguard/client_private.key
cat /etc/wireguard/client_public.key
```

**Скопируйте публичный ключ** - он понадобится для настройки сервера.

### 4. Создать конфигурацию WireGuard

```bash
cat > /etc/wireguard/wg0.conf << 'EOF'
[Interface]
Address = 10.0.0.2/24
PrivateKey = <ВАШ_ПРИВАТНЫЙ_КЛЮЧ>

[Peer]
PublicKey = I3fb8ObvsS+yDUZ1URkpZPgApnOuY/hfxowimbPa0nM=
Endpoint = 159.223.76.215:51820
AllowedIPs = 10.0.0.0/24
PersistentKeepalive = 25
EOF
```

Замените `<ВАШ_ПРИВАТНЫЙ_КЛЮЧ>` на содержимое `/etc/wireguard/client_private.key`.

### 5. Включить IP forwarding

```bash
sysctl -w net.ipv4.ip_forward=1
echo "net.ipv4.ip_forward=1" >> /etc/sysctl.conf
```

### 6. Запустить WireGuard

```bash
systemctl enable --now wg-quick@wg0
wg show
```

### 7. Обновить конфигурацию сервера

После того как получите публичный ключ клиента, выполните на **DO VM**:

```bash
# Добавить peer в конфигурацию
cat >> /etc/wireguard/wg0.conf << EOF

[Peer]
PublicKey = <ПУБЛИЧНЫЙ_КЛЮЧ_КЛИЕНТА>
AllowedIPs = 10.0.0.2/32, 192.168.66.0/24
EOF

# Перезапустить WireGuard
wg-quick down wg0
wg-quick up wg0
```

### 8. Проверить туннель

С DO VM выполните:

```bash
ping 10.0.0.2  # Должен пинговаться LXC
ping 192.168.66.142  # Должен пинговаться GPU сервер
ping 192.168.66.150  # Должен пинговаться n8n
```

## Использование PowerShell скриптов

Все скрипты находятся в `tunnelling/`:

### Просмотр всех пробросов

```powershell
.\list-forwards.ps1
```

### Добавить новый проброс

```powershell
.\add-forward.ps1 -Domain "newservice.denys.fast" -Target "192.168.66.142:8080"
```

С WebSocket поддержкой:

```powershell
.\add-forward.ps1 -Domain "ws.denys.fast" -Target "192.168.66.142:8080" -WebSocket
```

С автоматическим получением SSL:

```powershell
.\add-forward.ps1 -Domain "newservice.denys.fast" -Target "192.168.66.142:8080" -GetSSL
```

### Удалить проброс

```powershell
.\remove-forward.ps1 -Domain "oldservice.denys.fast"
```

### Изменить target существующего проброса

```powershell
.\edit-forward.ps1 -Domain "service.denys.fast" -Target "192.168.66.142:9000"
```

### Проверить статус туннеля

```powershell
.\status.ps1
```

## Настройка DNS в Cloudflare

**ВАЖНО:** Выполните это после настройки WireGuard туннеля.

1. Зайдите в Cloudflare Dashboard для домена `denys.fast`
2. Удалите старые CNAME записи, связанные с Cloudflare Tunnel
3. Создайте A-записи (Proxy OFF - серое облако):

| Имя | Тип | Значение | Proxy |
|-----|-----|----------|-------|
| n8n | A | 159.223.76.215 | OFF |
| gpu | A | 159.223.76.215 | OFF |
| apigencomf | A | 159.223.76.215 | OFF |
| simulationserverssh | A | 159.223.76.215 | OFF |
| simuqlator | A | 159.223.76.215 | OFF |
| tg | A | 159.223.76.215 | OFF |
| llm | A | 159.223.76.215 | OFF |
| interfaceblob | A | 159.223.76.215 | OFF |
| comfy1 | A | 159.223.76.215 | OFF |
| vllm | A | 159.223.76.215 | OFF |
| server1 | A | 159.223.76.215 | OFF |
| server1gpu | A | 159.223.76.215 | OFF |

**Proxy должен быть OFF**, иначе SSL от Let's Encrypt не сработает.

## Получение SSL сертификатов

После обновления DNS (подождите 1-5 минут), выполните на DO VM:

```bash
certbot --nginx -d n8n.denys.fast -d gpu.denys.fast -d apigencomf.denys.fast -d simuqlator.denys.fast -d tg.denys.fast -d llm.denys.fast -d interfaceblob.denys.fast -d comfy1.denys.fast -d vllm.denys.fast -d server1.denys.fast -d server1gpu.denys.fast --non-interactive --agree-tos --email admin@denys.fast
```

Или используйте PowerShell скрипт для каждого домена:

```powershell
.\add-forward.ps1 -Domain "n8n.denys.fast" -Target "192.168.66.150:5678" -GetSSL
```

## SSH доступ через туннель

SSH туннель настроен на порту 2222:

```bash
ssh -p 2222 user@159.223.76.215
```

Это подключит вас к `192.168.66.154:22` через туннель.

## Устранение проблем

### WireGuard не подключается

1. Проверьте firewall на DO VM: `ufw status`
2. Проверьте что порт 51820 открыт: `nc -u -v 159.223.76.215 51820`
3. Проверьте логи: `journalctl -u wg-quick@wg0 -f`

### Nginx не проксирует запросы

1. Проверьте конфигурацию: `nginx -t`
2. Проверьте логи: `tail -f /var/log/nginx/error.log`
3. Убедитесь что WireGuard туннель работает и внутренние IP доступны

### SSL сертификаты не выдаются

1. Убедитесь что DNS записи обновились: `nslookup n8n.denys.fast`
2. Убедитесь что Proxy в Cloudflare выключен (серое облако)
3. Проверьте что порт 80 открыт для Let's Encrypt: `ufw allow 80/tcp`

## Конфигурация

Все настройки находятся в `config.ps1`. При необходимости отредактируйте:

- `$VPS_IP` - IP адрес Digital Ocean VM
- `$VPS_USER` - пользователь для SSH
- `$VPS_PASSWORD` - пароль для SSH
