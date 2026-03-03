#!/bin/bash
# Скрипт для настройки WireGuard на LXC контейнере
# Выполните на LXC: bash setup_lxc.sh

set -e

echo "=== Остановка Cloudflare Tunnel ==="
systemctl stop cloudflared 2>/dev/null || true
systemctl disable cloudflared 2>/dev/null || true
docker stop cloudflared 2>/dev/null || true
docker rm cloudflared 2>/dev/null || true
echo "OK: Cloudflare остановлен"

echo ""
echo "=== Установка WireGuard ==="
apt update
apt install -y wireguard wireguard-tools
echo "OK: WireGuard установлен"

echo ""
echo "=== Генерация ключей ==="
wg genkey | tee /etc/wireguard/client_private.key | wg pubkey > /etc/wireguard/client_public.key
chmod 600 /etc/wireguard/client_private.key
echo "OK: Ключи сгенерированы"

echo ""
echo "=== ПУБЛИЧНЫЙ КЛЮЧ КЛИЕНТА (СКОПИРУЙТЕ ЕГО!) ==="
cat /etc/wireguard/client_public.key
echo "================================================"

echo ""
echo "=== Создание конфигурации WireGuard ==="
PRIVATE_KEY=$(cat /etc/wireguard/client_private.key)

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

echo "OK: Конфигурация создана"

echo ""
echo "=== Включение IP forwarding ==="
sysctl -w net.ipv4.ip_forward=1
echo "net.ipv4.ip_forward=1" >> /etc/sysctl.conf
echo "OK: IP forwarding включен"

echo ""
echo "=== Запуск WireGuard ==="
systemctl enable --now wg-quick@wg0
sleep 2

echo ""
echo "=== Статус WireGuard ==="
systemctl status wg-quick@wg0 --no-pager -l
echo ""
wg show

echo ""
echo "=== Проверка подключения ==="
ping -c 3 10.0.0.1 || echo "WARNING: Не удалось пинговать 10.0.0.1 (peer еще не добавлен на сервере)"

echo ""
echo "=== ГОТОВО! ==="
echo "Скопируйте публичный ключ выше и отправьте его для добавления peer на сервер"
