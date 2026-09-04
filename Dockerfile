# Compila el SPA. La imagen final no lleva Node: solo el dist.
# El layout replica al del repo porque vite.config.ts escribe en ../static/app,
# asi que el build cae en /src/static/app.
FROM node:22-slim AS frontend
WORKDIR /src/frontend

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build


FROM python:3.12-slim

# FFmpeg y ffprobe: el script los invoca como binarios del sistema.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY pyproject.toml ./
COPY app/ ./app/
RUN pip install --no-cache-dir .

COPY transcode.py ./
COPY static/ ./static/
COPY scripts/ ./scripts/

# Va despues de COPY static/ a proposito: lo sirve el mount /static que ya
# existe en main.py, sin backend nuevo. Queda en /static/app/index.html.
COPY --from=frontend /src/static/app ./static/app

# /media = videos de origen (montado read-only), /app/output = cache de
# artefactos (persistente: no se borra al arrancar).
RUN mkdir -p /app/output /media

# Necesario para que el progreso (\r) salga en tiempo real en docker logs.
ENV PYTHONUNBUFFERED=1

EXPOSE 8000

# Por defecto levanta la API FastAPI. Para modo CLI usar:
#   docker run stream-media python transcode.py /media/video.mkv --serve
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
