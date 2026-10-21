FROM node:22-bookworm-slim AS build
WORKDIR /src/Frontend
COPY Frontend/package*.json ./
RUN npm ci
COPY Frontend/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:stable-alpine
USER root
RUN apk add --no-cache openssl
COPY --from=build /src/Frontend/dist/ /usr/share/nginx/html/
COPY Frontend/docker/default.conf /etc/nginx/conf.d/default.conf
COPY Frontend/docker/10-local-tls.sh /docker-entrypoint.d/10-local-tls.sh
RUN chmod +x /docker-entrypoint.d/10-local-tls.sh
EXPOSE 8443
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
    CMD wget --no-check-certificate --quiet --spider https://127.0.0.1:8443/health || exit 1
USER 101
