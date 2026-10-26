docker build -f Backend/Dockerfile -t lewan24/octobackup-api:latest . --no-cache
docker build -f Frontend/Dockerfile -t lewan24/octobackup-web:latest . --no-cache

docker push lewan24/octobackup-api:latest
docker push lewan24/octobackup-web:latest
