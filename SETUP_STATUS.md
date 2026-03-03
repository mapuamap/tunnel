# Статус настройки WireGuard туннеля

## ✅ Выполнено

### Digital Ocean VM (159.223.76.215)

1. **WireGuard Server установлен и настроен**
   - Установлен: `wireguard`, `wireguard-tools`
   - Конфигурация: `/etc/wireguard/wg0.conf`
   - Серверный IP: `10.0.0.1/24`
   - Порт: `51820/udp`
   - Публичный ключ сервера: `I3fb8ObvsS+yDUZ1URkpZPgApnOuY/hfxowimbPa0nM=`
   - ⚠️ Peer (клиент) еще не добавлен - требуется публичный ключ от LXC

2. **Firewall (UFW) настроен**
   - Открыты порты: 22 (SSH), 80 (HTTP), 443 (HTTPS), 2222 (SSH tunnel), 51820/udp (WireGuard)
   - Forwarding настроен (требует проверки)

3. **Nginx установлен и настроен**
   - Все 11 HTTP сервисов настроены:
     - n8n.denys.fast → 192.168.66.150:5678 (с WebSocket)
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
   - SSH туннель настроен: порт 2222 → 192.168.66.154:22

4. **Certbot установлен**
   - Готов к получению SSL сертификатов после настройки DNS

5. **PowerShell скрипты созданы**
   - `config.ps1` - конфигурация
   - `list-forwards.ps1` - список пробросов
   - `add-forward.ps1` - добавить проброс
   - `remove-forward.ps1` - удалить проброс
   - `edit-forward.ps1` - изменить проброс
   - `status.ps1` - статус туннеля
   - `add-wg-peer.ps1` - добавить WireGuard peer

## ⚠️ Требует действий пользователя

### LXC контейнер (192.168.66.151)

**Проблема:** Автоматический SSH доступ не работает (аутентификация не проходит).

**Решение:** Выполните настройку вручную на LXC контейнере:

1. **Остановить Cloudflare Tunnel**
   ```bash
   systemctl stop cloudflared
   systemctl disable cloudflared
   # или если Docker:
   docker stop cloudflared && docker rm cloudflared
   ```

2. **Установить WireGuard**
   ```bash
   apt update && apt install -y wireguard wireguard-tools
   ```

3. **Сгенерировать ключи**
   ```bash
   wg genkey | tee /etc/wireguard/client_private.key | wg pubkey > /etc/wireguard/client_public.key
   chmod 600 /etc/wireguard/client_private.key
   cat /etc/wireguard/client_public.key
   ```
   **Скопируйте публичный ключ!**

4. **Создать конфигурацию WireGuard**
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
   Замените `<ВАШ_ПРИВАТНЫЙ_КЛЮЧ>` на содержимое `/etc/wireguard/client_private.key`

5. **Включить IP forwarding**
   ```bash
   sysctl -w net.ipv4.ip_forward=1
   echo "net.ipv4.ip_forward=1" >> /etc/sysctl.conf
   ```

6. **Запустить WireGuard**
   ```bash
   systemctl enable --now wg-quick@wg0
   wg show
   ```

7. **Добавить peer на сервер**
   
   После получения публичного ключа клиента, выполните на Windows:
   ```powershell
   .\add-wg-peer.ps1 -ClientPublicKey "<ПУБЛИЧНЫЙ_КЛЮЧ_КЛИЕНТА>"
   ```
   
   Или вручную на DO VM:
   ```bash
   cat >> /etc/wireguard/wg0.conf << EOF
   
   [Peer]
   PublicKey = <ПУБЛИЧНЫЙ_КЛЮЧ_КЛИЕНТА>
   AllowedIPs = 10.0.0.2/32, 192.168.66.0/24
   EOF
   wg-quick down wg0 && wg-quick up wg0
   ```

### DNS настройка в Cloudflare

После того как WireGuard туннель заработает:

1. Зайдите в Cloudflare Dashboard для домена `denys.fast`
2. Удалите старые CNAME записи Cloudflare Tunnel
3. Создайте A-записи (Proxy OFF - серое облако) для всех 12 поддоменов:
   - n8n, gpu, apigencomf, simulationserverssh, simuqlator, tg, llm, interfaceblob, comfy1, vllm, server1, server1gpu
   - Все указывают на: `159.223.76.215`
   - **Proxy должен быть OFF**

### SSL сертификаты

После обновления DNS (подождите 1-5 минут):

```bash
# На DO VM
certbot --nginx -d n8n.denys.fast -d gpu.denys.fast -d apigencomf.denys.fast -d simuqlator.denys.fast -d tg.denys.fast -d llm.denys.fast -d interfaceblob.denys.fast -d comfy1.denys.fast -d vllm.denys.fast -d server1.denys.fast -d server1gpu.denys.fast --non-interactive --agree-tos --email admin@denys.fast
```

Или используйте PowerShell скрипты для каждого домена отдельно.

## 📋 Проверка работы

После настройки LXC и добавления peer:

1. **Проверить WireGuard туннель:**
   ```bash
   # На DO VM
   ping 10.0.0.2  # LXC должен отвечать
   ping 192.168.66.142  # GPU сервер должен отвечать
   ```

2. **Проверить Nginx:**
   ```bash
   # На DO VM
   nginx -t
   systemctl status nginx
   ```

3. **Проверить доступность сервисов:**
   ```bash
   curl http://n8n.denys.fast  # Должен проксировать на 192.168.66.150:5678
   ```

4. **Использовать PowerShell скрипты:**
   ```powershell
   .\status.ps1  # Показать статус туннеля
   .\list-forwards.ps1  # Список всех пробросов
   ```

## 📁 Структура файлов

```
tunnelling/
├── config.ps1              # Конфигурация
├── list-forwards.ps1       # Список пробросов
├── add-forward.ps1         # Добавить проброс
├── remove-forward.ps1      # Удалить проброс
├── edit-forward.ps1        # Изменить проброс
├── status.ps1              # Статус туннеля
├── add-wg-peer.ps1         # Добавить WireGuard peer
├── _temp_ssh.py            # Вспомогательный SSH скрипт
├── _setup_nginx.py         # Скрипт настройки Nginx
├── README.md                # Документация
└── SETUP_STATUS.md         # Этот файл
```

## 🔧 Устранение проблем

### LXC не подключается к WireGuard

- Проверьте что модуль wireguard загружен на хосте Proxmox
- Проверьте firewall на LXC
- Проверьте логи: `journalctl -u wg-quick@wg0 -f`

### Внутренние IP не доступны с DO VM

- Проверьте IP forwarding на LXC: `sysctl net.ipv4.ip_forward`
- Проверьте маршруты: `ip route`
- Проверьте что LXC может пинговать внутренние IP

### Nginx не проксирует

- Проверьте что WireGuard туннель работает
- Проверьте логи Nginx: `tail -f /var/log/nginx/error.log`
- Проверьте что внутренние сервисы доступны через туннель
