#!/bin/bash
# 이 Mac에서 macOS 앱을 서명할 자체 서명 인증서를 만듭니다. 빌드가 바뀌어도 화면 기록과 손쉬운 사용 권한이 유지됩니다.
# 개인 키는 내보낼 수 없게 로그인 키체인에만 두고 저장소에 넣지 않습니다. 공개된 키로는 누구나 이 앱의 권한을 물려받을 수 있습니다.
# 시스템의 신뢰 설정은 바꾸지 않습니다. 다른 컴퓨터에서는 쓸 수 없고 배포용이 아닙니다.
set -euo pipefail

NAME="DevPostIt Local Signing"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"
PROPS="$ROOT/.tools/macos-signing.props"

if security find-identity -p codesigning "$KEYCHAIN" | grep -q "\"$NAME\""; then
  echo "이미 있습니다: $NAME"
else
  WORK="$(mktemp -d)"
  trap 'rm -rf "$WORK"' EXIT
  cat > "$WORK/cert.cnf" <<CONF
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = $NAME
[ext]
basicConstraints = critical, CA:FALSE
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
CONF
  PASS="$(/usr/bin/openssl rand -hex 16)"
  /usr/bin/openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -config "$WORK/cert.cnf" -keyout "$WORK/key.pem" -out "$WORK/cert.pem" 2>/dev/null
  /usr/bin/openssl pkcs12 -export -inkey "$WORK/key.pem" -in "$WORK/cert.pem" -out "$WORK/cert.p12" -passout "pass:$PASS"
  security import "$WORK/cert.p12" -k "$KEYCHAIN" -P "$PASS" -x -T /usr/bin/codesign
  /usr/bin/openssl x509 -in "$WORK/cert.pem" -outform der -out "$ROOT/scripts/macos-signing/DevPostIt-Local-Signing.cer"
  echo "만들었습니다: $NAME"
fi

mkdir -p "$(dirname "$PROPS")"
cat > "$PROPS" <<PROPS_XML
<Project>
  <PropertyGroup>
    <EnableCodeSigning>true</EnableCodeSigning>
    <CodesignKey>$NAME</CodesignKey>
  </PropertyGroup>
</Project>
PROPS_XML
echo "빌드 설정: $PROPS"
