# Tunnel Manager Configuration
# Edit these values according to your setup

$script:VPS_IP = "159.223.76.215"
$script:VPS_USER = "root"
$script:VPS_PASSWORD = "_X6NE_Eqz?hkGn"
$script:NGINX_CONFIG_DIR = "/etc/nginx/sites-available"
$script:NGINX_ENABLED_DIR = "/etc/nginx/sites-enabled"
$script:WG_PEER_IP = "10.0.0.2"  # LXC WireGuard IP

# Export configuration
Export-ModuleMember -Variable VPS_IP, VPS_USER, VPS_PASSWORD, NGINX_CONFIG_DIR, NGINX_ENABLED_DIR, WG_PEER_IP
