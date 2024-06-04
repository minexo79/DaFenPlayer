﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;
using DaFenPlayer.Video.Utilities;

namespace DaFenPlayer.Video.Decode
{
    internal unsafe partial class FFmpegHelp
    {
        event EventHandler<VideoReceiveArgs> OnVideoReceived;
        event EventHandler<LogArgs> OnLogReceived;

        // ffmpeg
        int errCode = 0;
        int videoStreamIndex = -1;
        AVStream* pStream;
        AVFormatContext * pFc;
        AVCodecID codec_id;
        AVCodecParameters pCodecParams;
        AVCodecContext* pCodecContext;
        SwsContext* pConvertContext;
        AVPixelFormat srcPixfmt = AVPixelFormat.AV_PIX_FMT_NONE;
        AVPixelFormat dstPixfmt = AVPixelFormat.AV_PIX_FMT_NONE;
        IntPtr convertedFrameBufferPtr = IntPtr.Zero;
        byte_ptrArray4 dstData;
        int_array4 dstLinesize;

        internal float width = 0;
        internal float height = 0;
        internal float framerate = 0;
        internal string codecName = "";

        // last frame time record
        internal DateTime lastFrameDateTime;

        // stream url
        string streamUrl = "";

        // log lock
        object logLock = new object();

        // task
        Task decodeTask;
        CancellationTokenSource cts;

        public FFmpegHelp(string _url, EventHandler<VideoReceiveArgs> onVideoReceived, EventHandler<LogArgs> onLogReceived)
        {
            streamUrl = _url;

            OnVideoReceived = onVideoReceived;
            OnLogReceived = onLogReceived;

            lastFrameDateTime = DateTime.Now;
            cts = new CancellationTokenSource();
        }

        internal void Dispose()
        {
            OnVideoReceived = null;
            OnLogReceived = null;

            GC.Collect();
        }

        // TODO: Prevent ExecutionEngineException
        void FFmpegLogCallback(void* ptr, int level, string fmt, byte* vl)
        {
            if (level > ffmpeg.av_log_get_level())
                return;

            var printPrefix = 1;
            var printLength = 1024;
            var printBuffer = stackalloc byte[printLength];
            ffmpeg.av_log_format_line(ptr, level, fmt, vl, printBuffer, printLength, &printPrefix);
            var message = Marshal.PtrToStringAnsi((IntPtr)printBuffer) ?? "";

            LogArgs logArgs = new LogArgs()
            {
                dateTime = DateTime.Now,
                logMessage = message
            };

            Console.Write(DateTime.Now + " " + message);

            if (OnLogReceived != null)
                OnLogReceived?.Invoke(this, logArgs);
        }

        void Register()
        {
            // find ffmpeg binaries
            FFmpegBinaryHelp.RegisterFFmpegBinaries();

            // ffmpeg init & Register
            ffmpeg.avformat_network_init();

            // set log 
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);

            // TODO: Prevent ExecutionEngineException
            //av_log_set_callback_callback callback = 
            //    (void* ptr, int level, string fmt, byte* vl) => FFmpegLogCallback(ptr, level, fmt, vl);

            //ffmpeg.av_log_set_callback(callback);
        }

        internal void StartFFmpeg()
        {
            Init();
            GetEncode();
            decodeTask = new Task(() => StartDecode(), cts.Token);
            decodeTask.Start();
        }

        void Init()
        {
            Console.WriteLine("==> FFmpeg init");
            Register();

            // set optimize flag
            AVDictionary* options = null;
            ffmpeg.av_dict_set(&options, "rtsp_transport", "tcp", 0);
            ffmpeg.av_dict_set(&options, "stimeout", "1000000", 0);         // max timeout 1 seconds
            ffmpeg.av_dict_set(&options, "fflags", "nobuffer", 0);          // no buffer
            ffmpeg.av_dict_set(&options, "fflags", "discardcorrupt", 0);    // discard corrupted frames
            ffmpeg.av_dict_set(&options, "flags", "low_delay", 0);          // no delay
            ffmpeg.av_dict_set(&options, "threads", "auto", 0);

            pFc = ffmpeg.avformat_alloc_context();
            AVFormatContext* pFcPtr = pFc;

            // open rtsp
            errCode = ffmpeg.avformat_open_input(&pFcPtr, streamUrl, null, &options);
            if (errCode < 0)
            {
                Console.WriteLine("==> Error: " + errCode);
                return;
            }

            // find stream info
            errCode = ffmpeg.avformat_find_stream_info(pFc, null);
            if (errCode < 0)
            {
                Console.WriteLine("==> Error: " + errCode);
                return;
            }
        }
        void GetEncode()
        {
            // find video stream
            //pStream = null;

            for (int i = 0; i < pFc->nb_streams; i++)
            {
                if (pFc->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    videoStreamIndex = i;
                    pStream = pFc->streams[i];
                    break;
                }
            }

            if (pStream == null)
            {
                Console.WriteLine("==> Could not found video stream!!");
                return;
            }

            // get context
            pCodecParams = *pStream->codecpar;

            // get pixel format, width, height
            width = pCodecParams.width;
            height = pCodecParams.height;
            srcPixfmt = (AVPixelFormat)pCodecParams.format;
            dstPixfmt = AVPixelFormat.AV_PIX_FMT_RGB0;

            // force to YUV420P
            if (srcPixfmt != AVPixelFormat.AV_PIX_FMT_YUV420P)
            {
                srcPixfmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            }

            // find codec_id
            codec_id = pCodecParams.codec_id;

            // declare convert context
            pConvertContext = ffmpeg.sws_getContext((int)width, (int)height, srcPixfmt,
                                                        (int)width, (int)height, dstPixfmt,
                                                        ffmpeg.SWS_FAST_BILINEAR, null, null, null);

            if (pConvertContext == null)
            {
                Console.WriteLine("==> Could not initialize the conversion context!!!");
                return;
            }

            // calculate dest buffer size
            int convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(dstPixfmt, (int)width, (int)height, 1);

            // allocate frame Pointer
            convertedFrameBufferPtr = Marshal.AllocHGlobal(convertedFrameBufferSize);
            dstData = new byte_ptrArray4();
            dstLinesize = new int_array4();

            // set graphic fill
            ffmpeg.av_image_fill_arrays(ref dstData, ref dstLinesize, (byte*)convertedFrameBufferPtr.ToPointer(),
                                        dstPixfmt, (int)width, (int)height, 1);
        }

        int StartDecode()
        {
            AVFormatContext * pFcPtr = pFc;
            AVCodecParameters _pCodecParams = pCodecParams;

            // find decoder using codec id
            AVCodec* pCodec = ffmpeg.avcodec_find_decoder(codec_id);
            if (pCodec == null)
            {
                Console.WriteLine("==> Codec not found!!");
                return -1;
            }

            codecName = UnsafeUtilities.PtrToStringUTF8(pCodec->name);
            framerate = pStream->r_frame_rate.num / (float)pStream->r_frame_rate.den;

            // allocate codec context
            AVCodecContext* pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);

            errCode = ffmpeg.avcodec_parameters_to_context(pCodecContext, &_pCodecParams);
            if (errCode < 0)
            {
                Console.WriteLine("==> Could not allocate codec context!!");
                return -1;
            }

            // ffmpeg.av_opt_set(pCodecContext->priv_data, "preset", "superfast", 0);

            if ((pCodec->capabilities & ffmpeg.AV_CODEC_CAP_TRUNCATED) == ffmpeg.AV_CODEC_CAP_TRUNCATED)
                pCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_TRUNCATED;

            // open codec
            errCode = ffmpeg.avcodec_open2(pCodecContext, pCodec, null);
            if (errCode < 0)
            {
                Console.WriteLine("==> Could not open codec!!");
                return -1;
            }

            AVPacket* pPacket = ffmpeg.av_packet_alloc();   // allocate packet
            AVFrame* pFrame = ffmpeg.av_frame_alloc();      // allocate frame

            do
            {
                ffmpeg.av_frame_unref(pFrame);
                ffmpeg.av_packet_unref(pPacket);

                //Console.WriteLine("1");
                if (ffmpeg.av_read_frame(pFcPtr, pPacket) != ffmpeg.AVERROR_EOF)
                {
                    //Console.WriteLine("2");
                    Decode(pCodecContext, pPacket, pFrame);
                    //Console.WriteLine("3");
                }

            } while (!cts.Token.IsCancellationRequested);


            ffmpeg.av_frame_free(&pFrame);
            ffmpeg.av_packet_free(&pPacket);

            return 0;
        }

        void Release()
        {
            Console.WriteLine("==> FFmpeg Stop");

            AVFormatContext* pFcPtr = pFc;
            AVCodecContext* pCodecContextPtr = pCodecContext;

            Marshal.FreeHGlobal(convertedFrameBufferPtr);

            ffmpeg.sws_freeContext(pConvertContext);
            ffmpeg.avcodec_free_context(&pCodecContextPtr);
            ffmpeg.avformat_close_input(&pFcPtr);

            width = 0;
            height = 0;
            framerate = 0;
            codecName = "";
        }

        internal void StopFFmpeg()
        {
            cts.Cancel();
            decodeTask.Wait();

            Release();
        }
    }
}