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

# /media = videos de origen (montado read-only), /app/output = cache de
# artefactos (persistente: no se borra al arrancar).
RUN mkdir -p /app/output /media

# Necesario para que el progreso (\r) salga en tiempo real en docker logs.
ENV PYTHONUNBUFFERED=1

EXPOSE 8000

# Por defecto levanta la API FastAPI. Para modo CLI usar:
#   docker run stream-media python transcode.py /media/video.mkv --serve
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
