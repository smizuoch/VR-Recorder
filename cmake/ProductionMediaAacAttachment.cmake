include_guard(GLOBAL)

# This module validates and returns the source side of the future production
# AAC attachment. It intentionally does not mutate a target or return a link
# list. When the actual Windows artifacts are admitted, their three
# FFmpeg::* identities must remain literal in src/VRRecorder.Native/CMakeLists
# so RepositoryNativeLinkVerifier can discover the third-party callsites.

function(vrrecorder_resolve_production_media_aac_sources output source_root)
    if(output STREQUAL "")
        message(FATAL_ERROR "Production AAC source output must be named")
    endif()
    if(source_root STREQUAL "" OR NOT IS_ABSOLUTE "${source_root}" OR
       NOT IS_DIRECTORY "${source_root}")
        message(
            FATAL_ERROR
            "Production AAC source root must be an existing absolute directory")
    endif()

    set(sources
        "${source_root}/src/ffmpeg_aac_audio_pipeline.cpp"
        "${source_root}/src/ffmpeg_aac_packet_encoder.cpp"
        "${source_root}/src/ffmpeg_libavcodec_encoder_port.cpp")
    foreach(source IN LISTS sources)
        if(NOT EXISTS "${source}" OR IS_DIRECTORY "${source}")
            message(
                FATAL_ERROR
                "Production AAC attachment source is missing: ${source}")
        endif()
    endforeach()
    set(${output} "${sources}" PARENT_SCOPE)
endfunction()

function(vrrecorder_require_production_media_aac_import_targets)
    foreach(link IN ITEMS
            FFmpeg::avcodec
            FFmpeg::avutil
            FFmpeg::swresample)
        if(NOT TARGET "${link}")
            message(
                FATAL_ERROR
                "Production AAC attachment requires canonical imported target ${link}")
        endif()
        get_target_property(aliased_target "${link}" ALIASED_TARGET)
        if(NOT aliased_target STREQUAL "aliased_target-NOTFOUND")
            message(
                FATAL_ERROR
                "Production AAC canonical target must not be an alias: ${link}")
        endif()
        get_target_property(imported "${link}" IMPORTED)
        if(NOT imported)
            message(
                FATAL_ERROR
                "Production AAC target must be imported: ${link}")
        endif()
    endforeach()
endfunction()

function(vrrecorder_resolve_production_media_aac_attachment output source_root)
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
            "Production AAC attachment requires exactly UNAVAILABLE or PRODUCTION; got '${VRRECORDER_MEDIA_FACTORY_VARIANT}'")
    endif()
    if(NOT VRRECORDER_ENABLE_FFMPEG_ADAPTERS)
        message(
            FATAL_ERROR
            "Production AAC attachment requires admitted production adapters")
    endif()

    vrrecorder_resolve_production_media_aac_sources(
        sources
        "${source_root}")
    vrrecorder_require_production_media_aac_import_targets()
    set(${output} "${sources}" PARENT_SCOPE)
endfunction()
