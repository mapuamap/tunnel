#!/usr/bin/env python3
"""Patch all existing Nginx configs to allow up to 50MB uploads."""
import sys
import subprocess
import re

try:
    import paramiko
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "paramiko", "--quiet"])
    import paramiko

TARGET_SIZE = "50m"
DIRECTIVE = f"client_max_body_size {TARGET_SIZE};"


def patch_configs(host, user, password):
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

    try:
        client.connect(host, username=user, password=password, timeout=10)
        sftp = client.open_sftp()

        stdin, stdout, _ = client.exec_command(
            "find /etc/nginx/sites-available -maxdepth 1 -type f ! -name default"
        )
        files = [f.strip() for f in stdout.read().decode().split("\n") if f.strip()]
        print(f"Found {len(files)} config(s) to patch")

        updated = 0
        for path in files:
            domain = path.split("/")[-1]
            with sftp.open(path, "r") as fh:
                content = fh.read().decode("utf-8")

            # Already has the directive — skip
            if "client_max_body_size" in content:
                # Update value if it's set to something other than 50m
                if DIRECTIVE in content:
                    print(f"  SKIP {domain} (already {TARGET_SIZE})")
                    continue
                # Different value → replace
                new_content = re.sub(
                    r"client_max_body_size\s+[^;]+;",
                    DIRECTIVE,
                    content,
                )
                with sftp.open(path, "w") as fh:
                    fh.write(new_content)
                print(f"  UPDATED {domain} (changed to {TARGET_SIZE})")
                updated += 1
                continue

            # Inject after the first `server_name ...;` line inside a server block
            new_content = re.sub(
                r"(server_name\s+[^;]+;)",
                rf"\1\n    {DIRECTIVE}",
                content,
                count=1,
            )
            if new_content == content:
                # Fallback: inject right after the opening `server {`
                new_content = re.sub(
                    r"(server\s*\{)",
                    rf"\1\n    {DIRECTIVE}",
                    content,
                    count=1,
                )

            with sftp.open(path, "w") as fh:
                fh.write(new_content)
            print(f"  PATCHED {domain}")
            updated += 1

        sftp.close()
        print(f"\nPatched {updated} file(s). Testing nginx...")

        stdin, stdout, stderr = client.exec_command("nginx -t")
        exit_status = stdout.channel.recv_exit_status()
        err = stderr.read().decode()

        if exit_status != 0:
            print(f"ERROR: nginx -t failed:\n{err}")
            return 1

        print("OK: nginx config valid")
        stdin, stdout, _ = client.exec_command("systemctl reload nginx")
        stdout.channel.recv_exit_status()
        print("OK: nginx reloaded")
        client.close()
        return 0

    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr)
        import traceback
        traceback.print_exc()
        return 1


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: python _fix_nginx_upload_size.py <host> <user> <password>", file=sys.stderr)
        sys.exit(1)
    sys.exit(patch_configs(sys.argv[1], sys.argv[2], sys.argv[3]))
