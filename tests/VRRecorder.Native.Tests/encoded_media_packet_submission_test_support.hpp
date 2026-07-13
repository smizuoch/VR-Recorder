#ifndef VRRECORDER_NATIVE_TEST_ENCODED_MEDIA_PACKET_SUBMISSION_SUPPORT_HPP
#define VRRECORDER_NATIVE_TEST_ENCODED_MEDIA_PACKET_SUBMISSION_SUPPORT_HPP

#include "encoded_media_packet_submission_port.hpp"

#include <cstddef>
#include <condition_variable>
#include <mutex>
#include <vector>

namespace vrrecorder::native::test {

class RecordingPacketSubmissionPort final
    : public EncodedMediaPacketSubmissionPort {
public:
    Mp4MuxResult SubmitBatch(
        MediaStreamKind producer,
        std::span<const EncodedMediaPacket> submitted_packets)
        noexcept override
    {
        producers.push_back(producer);
        batch_sizes.push_back(submitted_packets.size());
        packets.insert(
            packets.end(),
            submitted_packets.begin(),
            submitted_packets.end());
        const auto index = submit_calls++;
        if (index < scripted_submit_results.size()) {
            return scripted_submit_results[index];
        }
        return submit_result;
    }

    vrrec_status_t EncoderFinished(
        MediaStreamKind stream) noexcept override
    {
        finished_streams.push_back(stream);
        return encoder_finished_status;
    }

    void EncoderFailed(MediaStreamKind stream) noexcept override
    {
        failed_streams.push_back(stream);
    }

    std::vector<EncodedMediaPacket> packets;
    std::vector<MediaStreamKind> producers;
    std::vector<std::size_t> batch_sizes;
    std::vector<Mp4MuxResult> scripted_submit_results;
    std::vector<MediaStreamKind> finished_streams;
    std::vector<MediaStreamKind> failed_streams;
    Mp4MuxResult submit_result = Mp4MuxResult::Written;
    vrrec_status_t encoder_finished_status = VRREC_STATUS_OK;
    std::size_t submit_calls = 0;
};

class CoordinatedPacketSubmissionPort final
    : public EncodedMediaPacketSubmissionPort {
public:
    Mp4MuxResult SubmitBatch(
        MediaStreamKind producer,
        std::span<const EncodedMediaPacket> packets) noexcept override
    {
        const std::lock_guard lock(mutex);
        ++submit_calls;
        producers.push_back(producer);
        submitted_packet_count += packets.size();
        order.push_back(1);
        changed.notify_all();
        return terminal
            ? Mp4MuxResult::InvalidState
            : Mp4MuxResult::Written;
    }

    vrrec_status_t EncoderFinished(
        MediaStreamKind stream) noexcept override
    {
        const std::lock_guard lock(mutex);
        ++finished_calls;
        finished_streams.push_back(stream);
        order.push_back(2);
        changed.notify_all();
        return terminal
            ? VRREC_STATUS_INVALID_STATE
            : VRREC_STATUS_OK;
    }

    void EncoderFailed(MediaStreamKind stream) noexcept override
    {
        const std::lock_guard lock(mutex);
        terminal = true;
        ++failed_calls;
        failed_streams.push_back(stream);
        order.push_back(3);
        changed.notify_all();
    }

    std::mutex mutex;
    std::condition_variable changed;
    std::vector<MediaStreamKind> producers;
    std::vector<MediaStreamKind> finished_streams;
    std::vector<MediaStreamKind> failed_streams;
    std::vector<int> order;
    std::size_t submit_calls = 0;
    std::size_t submitted_packet_count = 0;
    std::size_t finished_calls = 0;
    std::size_t failed_calls = 0;
    bool terminal = false;
};

class BlockingSuccessfulCompletionPort final
    : public EncodedMediaPacketSubmissionPort {
public:
    Mp4MuxResult SubmitBatch(
        MediaStreamKind,
        std::span<const EncodedMediaPacket>) noexcept override
    {
        return Mp4MuxResult::Written;
    }

    vrrec_status_t EncoderFinished(
        MediaStreamKind stream) noexcept override
    {
        std::unique_lock lock(mutex);
        ++finished_calls;
        finished_stream = stream;
        finish_entered = true;
        changed.notify_all();
        changed.wait(lock, [&] { return release_finish; });
        return VRREC_STATUS_OK;
    }

    void EncoderFailed(MediaStreamKind stream) noexcept override
    {
        const std::lock_guard lock(mutex);
        ++failed_calls;
        failed_stream = stream;
        changed.notify_all();
    }

    std::mutex mutex;
    std::condition_variable changed;
    MediaStreamKind finished_stream = MediaStreamKind::Video;
    MediaStreamKind failed_stream = MediaStreamKind::Video;
    std::size_t finished_calls = 0;
    std::size_t failed_calls = 0;
    bool finish_entered = false;
    bool release_finish = false;
};

}

#endif
