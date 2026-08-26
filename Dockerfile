FROM python:3.12-slim

# FFmpeg y ffprobe: el script los invoca como binarios del sistema.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

RUN pip install --no-cache-dir structlog

COPY transcode.py ./
COPY app/ ./app/
COPY static/ ./static/

# /media = videos de origen (montado read-only), /app/output = segmentos HLS.
RUN mkdir -p /app/output /media

# Necesario para que el progreso (\r) salga en tiempo real en docker logs.
ENV PYTHONUNBUFFERED=1

EXPOSE 8000

ENTRYPOINT ["python", "transcode.py"]
CMD ["--help"]
