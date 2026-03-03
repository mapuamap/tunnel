# Команды для выполнения на LXC контейнере (192.168.66.151)

## 1. Добавить SSH ключ для автоматического доступа

Выполните эти команды на LXC контейнере:

```bash
# Создать директорию .ssh если её нет
mkdir -p ~/.ssh
chmod 700 ~/.ssh

# Добавить публичный ключ в authorized_keys
echo "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABgQDL+IbKbcd6Htp6rHXAGVuc8eLmDMCbMtVZsGqtUkpE2Ma+YvbfPEe+dMXc/SqZ39RKfagTukZdxk7TNKOA6Y2XrXDw7WruMPja+3/RJaHkVnCXsHB1ZeHmKWOphti2yL35CjQVzcqRev4+UathHK7kxbsukG98BfyqxN+aWQmDys20ObOvS2yJ6QIHPZAU360lAqVsecMNB8VBgUp5XFVlqZQEgaFBQjNtp9XWg9QCtJrBe9wREY2/+IF9Ta4F35+RZTIQe3Y7B0azGW1eC0680w2GYf/tkV78O9rRPaSvFk16kUACwKB4VWcycVCul0bN0TzRHk0K4PciHb5qjTO1der9XzbUFIqYf+lmo6YJSvN7UOjtsETuF76+ON5XOyr2J+DGIZ988I6VsIMpH9M0L7TOWDKYEGznONucRUdpMnTpJAYr8TUx5dWQ4xOcCiEbbLt8UPW1W6Qs2k8t9dZqyQnJyrqSQarg8aJevkUQnkC+9kThpbciN0UMm0k1WJ0= Denis@DESKTOP-05GB39A" >> ~/.ssh/authorized_keys

# Установить правильные права
chmod 600 ~/.ssh/authorized_keys

# Проверить что ключ добавлен
cat ~/.ssh/authorized_keys
```

## 2. Проверить доступность внутренних IP

```bash
# Проверить что LXC может пинговать все внутренние серверы
ping -c 2 192.168.66.142  # GPU сервер
ping -c 2 192.168.66.150  # n8n
ping -c 2 192.168.66.154  # Simulation server
ping -c 2 192.168.66.141  # Proxmox
```

## 3. Остановить Cloudflare Tunnel

```bash
# Если запущен как сервис
systemctl stop cloudflared
systemctl disable cloudflared

# Или если через Docker
docker ps | grep cloudflared
# Если найден, выполните:
docker stop cloudflared
docker rm cloudflared

# Проверить что Cloudflare больше не работает
systemctl status cloudflared
```

## 4. Установить WireGuard

```bash
apt update
apt install -y wireguard wireguard-tools
```

## 5. Сгенерировать ключи WireGuard

```bash
wg genkey | tee /etc/wireguard/client_private.key | wg pubkey > /etc/wireguard/client_public.key
chmod 600 /etc/wireguard/client_private.key

# Показать публичный ключ (СКОПИРУЙТЕ ЕГО!)
echo "=== ПУБЛИЧНЫЙ КЛЮЧ КЛИЕНТА (скопируйте его) ==="
cat /etc/wireguard/client_public.key
echo "================================================"
```

## 6. Создать конфигурацию WireGuard

```bash
# Получить приватный ключ
PRIVATE_KEY=$(cat /etc/wireguard/client_private.key)

# Создать конфигурацию
cat > /etc/wireguard/wg0.conf << EOF
[Interface]
Address = 10.0.0.2/24
PrivateKey = $PRIVATE_KEY

[Peer]
PublicKey = I3fb8ObvsS+yDUZ1URkpZPgApnOuY/hfxowimbPa0nM=
Endpoint = 159.223.76.215:51820
AllowedIPs = 10.0.0.0/24
PersistentKeepalive = 25
EOF

# Проверить конфигурацию
cat /etc/wireguard/wg0.conf
```

## 7. Включить IP forwarding

```bash
sysctl -w net.ipv4.ip_forward=1
echo "net.ipv4.ip_forward=1" >> /etc/sysctl.conf

# Проверить
sysctl net.ipv4.ip_forward
```

## 8. Запустить WireGuard

```bash
systemctl enable --now wg-quick@wg0

# Проверить статус
systemctl status wg-quick@wg0
wg show
```

## 9. Проверить подключение

```bash
# Проверить что туннель работает
ping -c 3 10.0.0.1  # Должен пинговаться DO VM

# Проверить что внутренние IP доступны через туннель (с DO VM)
# Это проверим после добавления peer на сервере
```

---

## После выполнения всех команд

1. **Скопируйте публичный ключ WireGuard клиента** (из шага 5)
2. **Сообщите мне**, что все команды выполнены
3. Я добавлю peer на сервер и проверю работу туннеля
