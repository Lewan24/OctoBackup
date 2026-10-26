FROM node:22-bookworm-slim AS build
WORKDIR /src/Frontend
COPY Frontend/package*.json ./
RUN npm ci
COPY Frontend/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:stable-alpine
COPY --from=build /src/Frontend/dist/ /usr/share/nginx/html/
COPY Frontend/docker/default.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
    CMD wget --quiet --spider http://127.0.0.1:8080/health || exit 1
USER 101
