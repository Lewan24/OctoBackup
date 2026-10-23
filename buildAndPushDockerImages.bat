docker build -t lewan24/octobackup-api:latest ./Backend --no-cache
docker build -t lewan24/octobackup-web:latest ./Frontend --no-cache

docker push lewan24/octobackup-api:latest
docker push lewan24/octobackup-web:latest
