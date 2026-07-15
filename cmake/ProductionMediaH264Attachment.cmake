include_guard(GLOBAL)

# Validates the source side of the future Windows production software-H.264
# attachment. Literal FFmpeg::* link callsites remain owned by the main native
# CMakeLists once actual admitted Windows artifacts are available.

function(vrrecorder_resolve_production_media_h264_sources output source_root)
    if(output STREQUAL "")
        message(FATAL_ERROR "Production H264 source output must be named")
    endif()
    if(source_root STREQUAL "" OR NOT IS_ABSOLUTE "${source_root}" OR
       NOT IS_DIRECTORY "${source_root}")
        message(
            FATAL_ERROR
            "Production H264 source root must be an existing absolute directory")
    endif()

    set(sources
        "${source_root}/src/ffmpeg_h264_packet_encoder.cpp"
        "${source_root}/src/ffmpeg_h264_media_foundation_configuration.cpp"
        "${source_root}/src/ffmpeg_h264_nv12_frame.cpp"
        "${source_root}/src/ffmpeg_libavcodec_encoder_port.cpp")
    foreach(source IN LISTS sources)
        if(NOT EXISTS "${source}" OR IS_DIRECTORY "${source}")
            message(
                FATAL_ERROR
                "Production H264 attachment source is missing: ${source}")
        endif()
    endforeach()
    set(${output} "${sources}" PARENT_SCOPE)
endfunction()

function(vrrecorder_require_production_media_h264_import_targets)
    foreach(link IN ITEMS FFmpeg::avcodec FFmpeg::avutil)
        if(NOT TARGET "${link}")
            message(
                FATAL_ERROR
                "Production H264 attachment requires canonical imported target ${link}")
        endif()
        get_target_property(aliased_target "${link}" ALIASED_TARGET)
        if(NOT aliased_target STREQUAL "aliased_target-NOTFOUND")
            message(
                FATAL_ERROR
                "Production H264 canonical target must not be an alias: ${link}")
        endif()
        get_target_property(imported "${link}" IMPORTED)
        if(NOT imported)
            message(
                FATAL_ERROR
                "Production H264 target must be imported: ${link}")
        endif()
    endforeach()
endfunction()

function(vrrecorder_resolve_production_media_h264_attachment output source_root)
    if(NOT DEFINED VRRECORDER_MEDIA_FACTORY_VARIANT)
        message(FATAL_ERROR "VRRECORDER_MEDIA_FACTORY_VARIANT must be defined")
    endif()
    if(VRRECORDER_MEDIA_FACTORY_VARIANT STREQUAL "UNAVAILABLE")
        set(${output} "" PARENT_SCOPE)
        return()
    endif()
    if(NOT VRRECORDER_MEDIA_FACTORY_VARIANT STREQUAL "PRODUCTION")
        message(
            FATAL_ERROR
            "Production H264 attachment requires exactly UNAVAILABLE or PRODUCTION; got '${VRRECORDER_MEDIA_FACTORY_VARIANT}'")
    endif()
    if(NOT VRRECORDER_ENABLE_FFMPEG_ADAPTERS)
        message(
            FATAL_ERROR
            "Production H264 attachment requires admitted production adapters")
    endif()

    vrrecorder_resolve_production_media_h264_sources(
        sources
        "${source_root}")
    vrrecorder_require_production_media_h264_import_targets()
    set(${output} "${sources}" PARENT_SCOPE)
endfunction()
